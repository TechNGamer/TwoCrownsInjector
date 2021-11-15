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

		/* As the name says, it assumes the default steam location of where the game might be.
		 * So for Windows, the Program Files (x86) location. For macOS, the Library/Application Support location.
		 * For Linux, it assumes the .steam folder in the user's home partition. */
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

		/* This guesses, but is an accurate assumption, of where the game has it's save located at.
		 * Unity has the location of `Application.persistantData` located at the locations that are
		 * returned by this property. The reason I am using a property is for the fact that is is basic,
		 * doesn't process any information from the class itself, returns a core type, and that it would
		 * look weird to make it a method. */
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

		// These guys are fields since they are simply storing data, no processing and not presenting to other classes/objects.
		private static bool forceRePatch;

		private static string location = AssumeLocation;

		private static void Main( string[] args ) {
			// Assume that the user wants to interact with the program.
			var interactive = true;

			// Processes the arguments. The only supported arguments is --game-location (with the next being a path), --no-interactive, and -f/--force for force repatching.
			for ( var i = 0; i < args.Length; ++i ) {
				if ( args[i] == "--game-location" ) {
					location = args[++i];
				} else if ( args[i] == "--no-interactive" ) {
					interactive = false;
				} else if ( args[i] == "-f" || args[i] == "--force" ) {
					forceRePatch = true;
				}
			}

			if ( interactive ) {
				Interactive();
			} else {
				if ( !Directory.Exists( location ) ) {
					throw new DirectoryNotFoundException( "The provided directory does not exist. Please make sure you pointed the program to the proper place." );
				}

				StartPatching( Path.Combine( location, "KingdomTwoCrowns_Data", "Managed" ) );
			}
		}

		private static void Interactive() {
			var loc = location;

			// This section tells the user what this program is, and to also back up their saves.
			Console.WriteLine( "Welcome to the Kingdom: Two Crowns Mod Patcher." );
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine( $"Back up your save files before doing this.\nLocation is `{GameSaveLocation}`".Blinking() );
			Console.ResetColor();
			Console.WriteLine( "Press any key to continue. . ." );
			Console.ReadKey();

			// Keeps asking the user to input a valid path.
			while ( string.IsNullOrWhiteSpace( loc ) || !Directory.Exists( loc ) ) {
				Console.WriteLine( $"Please enter the full location of the game. Example: {AssumeLocation}\n" );

				loc = Console.ReadLine();
			}
			
			Debug.Assert( loc != null, nameof( loc ) + " != null" );

			/* Where the magic happens where it will start patching a Unity library.
			 * The reason for patching a Unity library instead of the `Assembly-CSharp.dll`
			 * is because there are no methods that are guaranteed to be called on the start
			 * of the game. However, if we patch `Application`, which is a frequently used class,
			 * then there is a high chance of our patch being applied to and working. Look at
			 * the method to see how this is accomplished. */
			StartPatching( Path.Combine( loc, "KingdomTwoCrowns_Data", "Managed" ) );

			Console.WriteLine( "Injecting complete, have fun modding.\nPress any key to continue. . ." );

			Console.ReadKey();
		}

		private static void StartPatching( string managedLocation ) {
			// This section is grabbing everything that is needed for patching.
			var unityCore  = Path.Combine( managedLocation, "UnityEngine.CoreModule.dll" );
			var modLibrary = GetModLibraryPath();
			var modsFolder = Path.Combine( managedLocation, "..", "..", "Mods" );

			// Checks to see if the mod directory exists and creates one if it isn't. Why? To make sure it is there.
			if ( !Directory.Exists( modsFolder ) ) {
				_ = Directory.CreateDirectory( modsFolder );
			}

			Console.WriteLine( "Coping needed libraries over." );

			/* The mod loader also bundles Harmony with it, so that is why 2 assemblies are getting copied.
			 * Harmony allows mods to patch objects at runtime without being destructive, like we're doing.
			 * Since we do not have any openings to begin with, we have to be destructive. */
			File.Copy( modLibrary, Path.Combine( managedLocation, MOD_LIBRARY_NAME ), true );
			File.Copy( GetHarmonyPath(), Path.Combine( managedLocation, HARMONY_LIBRARY_NAME ), true );

			Console.WriteLine( "Checking to see if UnityEngine.CoreModule.dll is patched." );

			var resolver = new DefaultAssemblyResolver();

			// We want the resolver to look at the managed folder of the game, since that is where all the managed DLL's are at.
			resolver.AddSearchDirectory( managedLocation );
			
			// This section just opens UnityEngine.CoreModule.dll and also looks to see if it is already patched.
			var readerParams = new ReaderParameters() {
				AssemblyResolver = resolver
			};
			var asmStream = new FileStream( unityCore, FileMode.Open );
			var unityDef  = AssemblyDefinition.ReadAssembly( asmStream, readerParams );

			if ( !forceRePatch && IsAlreadyInjected( unityDef ) ) {
				unityDef.Dispose();
				asmStream.Dispose();

				Console.WriteLine( "UnityEngine.CoreModule.dll is already patched." );

				return;
			}

			Console.WriteLine( "Patching UnityEngine.CoreModule.dll." );

			/* This section is where it is fun, and also complicated. It first creates a new AssemblyDefinition for
			 * the mod loader assembly. It then locates the class called `LoaderEntry`, then the method `BeginModLoading`
			 * where, as the name implies, it begins loading of the mods. Once it has finished that, we then create a
			 * MethodReference from the Unity AssemblyDefinition to the method we want to call. We then look for the
			 * type `Application` inside `UnityEngine.CoreModule.dll`. We also need a TypeReference which would be the
			 * return type. But since we are going to be calling this method from the static constructor, it's return
			 * type is going to be void. Lastly for this giant variable block, we get the IL Processor. */
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

			// We inject the call operand code into the static constructor, then a return since we don't want it to do things beyond the method.
			ilProc.Append( ilProc.Create( OpCodes.Call, injectRef ) );
			ilProc.Append( ilProc.Create( OpCodes.Ret ) );

			/* We then write the newly created codes out to the list of methods of that class.
			 * Write the changes we made to the assembly, then dispose of everything that needs disposing of.
			 * And now this method is done. */
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

		// This method assumes that the program's Current Directory is the same as where it's assemblies are at.
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

		// Little trick I learned to make a line on the terminal blink. It doesn't seem to work on Windows.
		private static string Blinking( this string str ) {
			if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ) {
				return str;
			}

			return "\x1b[5m" + str + "\x1b[25m";
		}
	}
}
