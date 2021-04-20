﻿using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace x360ce.App
{
	static partial class Program
	{

		public static bool IsDebug
		{
			get
			{
#if DEBUG
				return true;
#else
				return false;
#endif
			}
		}

		internal const int STATE_VISIBLE = 0x00000002;
		internal const int STATE_ENABLED = 0x00000004;

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			// Fix: System.TimeoutException: The operation has timed out. at System.Windows.Threading.Dispatcher.InvokeImpl
			AppContext.SetSwitch("Switch.MS.Internal.DoNotInvokeInWeakEventTableShutdownListener", true);
			// First: Set working folder to the path of executable.
			var fi = new FileInfo(System.Windows.Forms.Application.ExecutablePath);
			Directory.SetCurrentDirectory(fi.Directory.FullName);
			// Prevent brave users from running this application from Windows folder.
			var winFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
			if (fi.FullName.StartsWith(winFolder, StringComparison.OrdinalIgnoreCase))
			{
				MessageBox.Show("Running from Windows folder is not allowed!\r\nPlease run this program from another folder.",
					"Windows Folder", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			// IMPORTANT: Make sure this class don't have any static references to x360ce.Engine library or
			// program tries to load x360ce.Engine.dll before AssemblyResolve event is available and fails.
			AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
			// In debug mode don't try to wrap app into try catch.
			if (IsDebug)
			{
				StartApp(args);
				return;
			}
			try
			{
				StartApp(args);
			}
			catch (Exception ex)
			{
				var message = ExceptionToText(ex);
				if (message.Contains("Could not load file or assembly 'Microsoft.DirectX"))
				{
					message += "===============================================================\r\n";
					message += "You can download Microsoft DirectX from:\r\n";
					message += "http://www.microsoft.com/en-us/download/details.aspx?id=35";
				}
				var result = MessageBox.Show(message, "Exception!", MessageBoxButton.OKCancel, MessageBoxImage.Error, MessageBoxResult.OK);
				if (result == MessageBoxResult.Cancel)
					app.Shutdown();
			}
		}

		public const string arg_WindowState = "WindowState";

		internal class NativeMethods
		{
			[System.Runtime.InteropServices.DllImport("user32.dll")]
			internal static extern bool SetProcessDPIAware();
		}

		static void StartApp(string[] args)
		{
			if (!RuntimePolicyHelper.LegacyV2RuntimeEnabledSuccessfully)
			{
				// Failed to enable useLegacyV2RuntimeActivationPolicy at runtime.
			}
			// Requires System.Configuration.Installl reference.
			var ic = new System.Configuration.Install.InstallContext(null, args);
			// ------------------------------------------------
			// Administrator commands.
			// ------------------------------------------------
			var executed = ProcessAdminCommands(args);
			// If valid command was executed then...
			if (executed)
				return;
			// ------------------------------------------------
			if (ic.Parameters.ContainsKey("Settings"))
			{
				OpenSettingsFolder(ApplicationDataPath);
				OpenSettingsFolder(CommonApplicationDataPath);
				OpenSettingsFolder(LocalApplicationDataPath);
				return;
			}
			if (!CheckSettings())
				return;
			var o = SettingsManager.Options;
			// Must be called before you create the application window.
			if (Environment.OSVersion.Version.Major >= 6 && o.IsProcessDPIAware)
				NativeMethods.SetProcessDPIAware();
			Global.InitializeServices();
			Global.InitializeCloudClient();


			app = new App();
			//app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
			//app.Run(Global._MainWindow);
			app.InitializeComponent();
			app.Run();

			//Global._MainWindow.Show();
			//Application.Run(Global._MainWindow);

			/*
			  
			  //System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Threading.SynchronizationContext());
			//Global._MainWindow = new MainWindow();
			if (ic.Parameters.ContainsKey("Exit"))
			{
				// Close all x360ce apps.
				StartHelper.BroadcastMessage(StartHelper.wParam_Close);
				return;
			}



			var doNotAllowToRun = o.AllowOnlyOneCopy && StartHelper.BroadcastMessage(StartHelper.wParam_Restore);
			// If one copy is already opened then...
			if (doNotAllowToRun)
			{
				// Dispose properly so that the tray icon will be removed.
				Global._MainWindow.Dispose();
			}
			else
			{
				//MainForm.TrayNotifyIcon.Visible = true;
				if (ic.Parameters.ContainsKey(arg_WindowState))
				{
					switch (ic.Parameters[arg_WindowState])
					{
						case "Maximized":
							Global._MainWindow.RestoreFromTray();
							Global._MainWindow.WindowState = System.Windows.WindowState.Maximized;
							break;
						case "Minimized":
							Global._MainWindow.MinimizeToTray(false, o.MinimizeToTray);
							break;
					}
				}
				app = new App();
				//app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
				//app.Run(Global._MainWindow);
				//app.InitializeComponent();
				app.Run();
				//Global._MainWindow.Show();
				//Application.Run(Global._MainWindow);
			}

			*/

			Global.DisposeCloudClient();
			Global.DisposeServices();
		}

		static App app;

		//static void DispatchToApp(Action action)
		//{
		//	app.Dispatcher.Invoke(action);
		//}

		public static bool IsClosing;

		public static object DeviceLock = new object();

		public static int TimerCount = 0;
		public static int ReloadCount = 0;
		public static int ErrorCount = 0;

		public static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
		{
			if (IsClosing)
				return;
			ErrorCount++;
			Global._MainWindow.UpdateTimer.Stop();
			Global._MainWindow.UpdateStatus("- " + e.Exception.Message);
			Global._MainWindow.UpdateTimer.Start();
		}

		static void OpenSettingsFolder(string path)
		{
			var di = new DirectoryInfo(path);
			//if (!di.Exists) return;
			//if (di.GetFiles().Length == 0) return;
			var psi = new ProcessStartInfo(di.Parent.Parent.FullName);
			psi.UseShellExecute = true;
			psi.ErrorDialog = true;
			Process.Start(psi);
		}

		static bool CheckSettings()
		{
			try
			{
				Properties.Settings.Default.Reload();
			}
			catch (ConfigurationErrorsException ex)
			{
				// Requires System.Configuration
				string filename = ((ConfigurationErrorsException)ex.InnerException).Filename;
				var title = "Corrupt user settings of " + Product;
				var text =
					"Program has detected that your user settings file has become corrupted. " +
					"This may be due to a crash or improper exiting of the program. " +
					"Program must reset your user settings in order to continue.\r\n" +
					"Click [Yes] to reset your user settings and continue.\r\n" +
					"Click [No] if you wish to exit and attempt manual repair.";
				var result = MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Error);
				if (result == MessageBoxResult.Yes)
				{
					File.Delete(filename);
					Properties.Settings.Default.Reload();
				}
				else
				{
					OpenSettingsFolder(ApplicationDataPath);
					OpenSettingsFolder(CommonApplicationDataPath);
					OpenSettingsFolder(LocalApplicationDataPath);
					return false;
				}
			}
			return true;
		}

		static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs e)
		{
			var dllName = e.Name.Contains(",") ? e.Name.Substring(0, e.Name.IndexOf(',')) : e.Name.Replace(".dll", "");
			Stream sr = null;
			switch (dllName)
			{
				case "Xceed.Wpf.AvalonDock":
				case "Xceed.Wpf.AvalonDock.Themes.Aero":
				case "Xceed.Wpf.AvalonDock.Themes.Metro":
				case "Xceed.Wpf.AvalonDock.Themes.VS2010":
				case "Xceed.Wpf.Toolkit":
				case "ViGEmClient":
				case "x360ce.Engine":
				case "x360ce.Engine.XmlSerializers":
				case "SharpDX":
				case "SharpDX.DirectInput":
				case "SharpDX.RawInput":
					sr = GetResourceStream(dllName + ".dll");
					break;
				default:
					break;
			}
			if (sr == null)
				return null;
			var bytes = new byte[sr.Length];
			sr.Read(bytes, 0, bytes.Length);
			var asm = Assembly.Load(bytes);
			sr.Dispose();
			return asm;
		}

		/// <summary>
		/// Get 32-bit or 64-bit resource depending on x360ce.exe platform.
		/// </summary>
		public static Stream GetResourceStream(string name)
		{
			var path = GetResourcePath(name);
			if (path == null)
				return null;
			var assembly = Assembly.GetEntryAssembly();
			if (assembly == null)
				return null;
			var sr = assembly.GetManifestResourceStream(path);
			return sr;
		}

		/// <summary>
		/// Get 32-bit or 64-bit resource depending on x360ce.exe platform.
		/// </summary>
		public static string GetResourcePath(string name)
		{
			var assembly = Assembly.GetEntryAssembly();
			if (assembly == null)
				return null;
			var names = assembly.GetManifestResourceNames()
				.Where(x => x.EndsWith(name));
			var a = Environment.Is64BitProcess ? ".x64." : ".x86.";
			// Try to get by architecture first.
			var path = names.FirstOrDefault(x => x.Contains(a));
			if (!string.IsNullOrEmpty(path))
				return path;
			// Return first found.
			return names.FirstOrDefault();
		}

		#region ■ ExceptionToText

		// Exception to string needed here so that links to other references won't be an issue.

		static string ExceptionToText(Exception ex)
		{
			var message = "";
			AddExceptionMessage(ex, ref message);
			if (ex.InnerException != null) AddExceptionMessage(ex.InnerException, ref message);
			return message;
		}

		/// <summary>Add information about missing libraries and DLLs</summary>
		static void AddExceptionMessage(Exception ex, ref string message)
		{
			var ex1 = ex as ConfigurationErrorsException;
			var ex2 = ex as ReflectionTypeLoadException;
			var m = "";
			if (ex1 != null)
			{
				m += string.Format("FileName: {0}\r\n", ex1.Filename);
				m += string.Format("Line: {0}\r\n", ex1.Line);
			}
			else if (ex2 != null)
			{
				foreach (Exception x in ex2.LoaderExceptions) m += x.Message + "\r\n";
			}
			if (message.Length > 0)
			{
				message += "===============================================================\r\n";
			}
			message += ex.ToString() + "\r\n";
			foreach (var key in ex.Data.Keys)
			{
				m += string.Format("{0}: {1}\r\n", key, ex1.Data[key]);
			}
			if (m.Length > 0)
			{
				message += "===============================================================\r\n";
				message += m;
			}
		}

		#endregion

		#region GetInfo

		private static string ApplicationDataPath
			=> Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		private static string CommonApplicationDataPath
			=> Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
		private static string LocalApplicationDataPath
			=> Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		//private static string Company { get { return GetAttribute<AssemblyCompanyAttribute>(a => a.Company); } }
		private static string Product { get { return GetAttribute<AssemblyProductAttribute>(a => a.Product); } }

		// Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

		private static string GetAttribute<T>(Func<T, string> value) where T : Attribute
		{
			var asm = Assembly.GetExecutingAssembly();
			T attribute = (T)Attribute.GetCustomAttribute(asm, typeof(T));
			return attribute == null
				? ""
				: value.Invoke(attribute);
		}

		#endregion



	}
}
