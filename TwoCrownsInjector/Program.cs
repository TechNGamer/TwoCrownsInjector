using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TwoCrownsInjector {
	internal static class Program {
		private const string MOD_LIBRARY_NAME     = "BasicModLoader.dll";
		private const string HARMONY_LIBRARY_NAME = "0Harmony.dll";

		private static readonly Exception NOT_FOUND_EXCEPTION = new FileNotFoundException( "Attempted to grab path for mod loader, but got nothing." );

		private static string AssumeLocation {
			get {
				if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ) {
					return @"C:\Program Files (x86)\Steam\steamapps\common\Kingdom Two Crowns";
				}

				if ( RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) ) {
					return Path.Combine(
						Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ),
						"Library", "Application Support", "Steam", "steamapps", "common", "Kingdom Two Crowns"
					);
				}

				return Path.Combine(
					Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ),
					".steam", "root", "steamapps", "common", "Kingdom Two Crowns"
				);
			}
		}

		private static string GameSaveLocation {
			get {
				if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ) {
					return Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), "AppData", "LocalLow", "noio" );
				}

				if ( RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) ) {
					return Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ), "unity3d", "noio" );
				}

				// I do not care to make it a since return statement as that would be a very long line.
				// ReSharper disable once ConvertIfStatementToReturnStatement
				if ( RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) ) {
					return Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), "Library", "Application Support", "noio" );
				}

				return string.Empty;
			}
		}

		private static bool ForceRePatch { get; set; }

		private static string Location { get; set; } = AssumeLocation;

		private static void Main( string[] args ) {
			var interactive = true;

			#if DEBUG
			Console.WriteLine( $"My working directory is `{Environment.CurrentDirectory}`." );
			#endif

			for ( var i = 0; i < args.Length; ++i ) {
				if ( args[i] == "--game-location" ) {
					Location = args[++i];
				} else if ( args[i] == "--no-interactive" ) {
					interactive = false;
				} else if ( args[i] == "-f" || args[i] == "--force" ) {
					ForceRePatch = true;
				}
			}

			if ( interactive ) {
				Interactive();
			} else {
				if ( !Directory.Exists( Location ) ) {
					throw new DirectoryNotFoundException( "The provided directory does not exist. Please make sure you pointed the program to the proper place." );
				}

				StartPatching( Path.Combine( Location, "KingdomTwoCrowns_Data", "Managed" ) );
			}
		}

		private static void Interactive() {
			var loc = Location;

			// This section tells the user what this program is, and to also back up their saves.
			Console.WriteLine( "Welcome to the Kingdom: Two Crowns Mod Patcher." );

			Console.ForegroundColor = ConsoleColor.Red;

			Console.WriteLine( $"Back up your save files before doing this.\nLocation is `{GameSaveLocation}`".Blinking() );

			Console.ResetColor();

			Console.WriteLine( "Press any key to continue. . ." );

			Console.ReadKey();

			while ( string.IsNullOrWhiteSpace( loc ) || !Directory.Exists( loc ) ) {
				Console.WriteLine( $"Please enter the full location of the game. Example: {AssumeLocation}\n" );

				loc = Console.ReadLine();
			}

			Debug.Assert( loc != null, nameof( loc ) + " != null" );

			StartPatching( Path.Combine( loc, "KingdomTwoCrowns_Data", "Managed" ) );

			Console.WriteLine( "Injecting complete, have fun modding." );

			Console.ReadKey();
		}

		private static void StartPatching( string managedLocation ) {
			var unityCore  = Path.Combine( managedLocation, "UnityEngine.CoreModule.dll" );
			var modLibrary = GetModLibraryPath();
			var modsFolder = Path.Combine( managedLocation, "..", "..", "Mods" );

			if ( !Directory.Exists( modsFolder ) ) {
				_ = Directory.CreateDirectory( modsFolder );
			}

			Console.WriteLine( "Coping needed libraries over." );

			File.Copy( modLibrary, Path.Combine( managedLocation, MOD_LIBRARY_NAME ), true );
			File.Copy( GetHarmonyPath(), Path.Combine( managedLocation, HARMONY_LIBRARY_NAME ), true );

			Console.WriteLine( "Checking to see if UnityEngine.CoreModule.dll is patched." );

			var resolver = new DefaultAssemblyResolver();

			resolver.AddSearchDirectory( managedLocation );

			var readerParams = new ReaderParameters() {
				AssemblyResolver = resolver
			};
			var asmStream = new FileStream( unityCore, FileMode.Open );
			var unityDef  = AssemblyDefinition.ReadAssembly( asmStream, readerParams );

			if ( !ForceRePatch && IsAlreadyInjected( unityDef ) ) {
				unityDef.Dispose();
				asmStream.Dispose();

				Console.WriteLine( "UnityEngine.CoreModule.dll is already patched." );

				return;
			}

			Console.WriteLine( "Patching UnityEngine.CoreModule.dll." );

			var injected = AssemblyDefinition.ReadAssembly( MOD_LIBRARY_NAME, readerParams );
			var injectMethod = injected.MainModule.Types.First( t => t.Name == "LoaderEntry" )
				.Methods.First( m => m.Name == "BeginModLoading" );
			var injectRef   = unityDef.MainModule.ImportReference( injectMethod );
			var application = unityDef.MainModule.Types.First( t => t.Name == "Application" );
			var voidType    = unityDef.MainModule.ImportReference( typeof( void ) );
			var classCon = new MethodDefinition(
				".cctor",
				MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
				voidType
			);
			var ilProc = classCon.Body.GetILProcessor();

			ilProc.Append( ilProc.Create( OpCodes.Call, injectRef ) );
			ilProc.Append( ilProc.Create( OpCodes.Ret ) );

			application.Methods.Add( classCon );

			unityDef.Write();

			injected.Dispose();
			unityDef.Dispose();
			asmStream.Dispose();
		}

		private static bool IsAlreadyInjected( AssemblyDefinition unityDefinition ) => unityDefinition
			.MainModule.AssemblyReferences.Any( a => a.Name.Contains( "BasicModLoader" ) );

		private static string GetHarmonyPath() {
			var myAsm   = GetAssemblyPath();
			var harmony = new FileInfo( myAsm ).Directory?.GetFiles().FirstOrDefault( f => f.Name == HARMONY_LIBRARY_NAME );

			if ( harmony == null ) {
				throw NOT_FOUND_EXCEPTION;
			}

			return harmony.FullName;
		}

		private static string GetModLibraryPath() {
			var myAsm     = GetAssemblyPath();
			var modLoader = new FileInfo( myAsm ).Directory?.GetFiles().FirstOrDefault( f => f.Name == MOD_LIBRARY_NAME );

			if ( modLoader == null ) {
				throw NOT_FOUND_EXCEPTION;
			}

			return modLoader.FullName;
		}

		private static string GetAssemblyPath() {
			var myAsm         = Environment.CurrentDirectory;
			var processModule = Process.GetCurrentProcess().MainModule;
			if ( Directory.Exists( myAsm ) ) {
				// Doing this as a bit of a work around.
				return Path.Combine( myAsm, "Something.dll" );
			}

			if ( processModule != null && !string.IsNullOrWhiteSpace( processModule.FileName ) ) {
				myAsm = processModule.FileName;
			} else {
				myAsm = typeof( Program ).Assembly.Location;

				if ( string.IsNullOrWhiteSpace( myAsm ) ) {
					return ".";
				}
			}

			return myAsm;
		}

		private static string Blinking( this string str ) {
			if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ) {
				return str;
			}

			return "\x1b[5m" + str + "\x1b[25m";
		}
	}
}
