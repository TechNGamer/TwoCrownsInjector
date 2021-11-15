using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace InjectorUI {
	[SuppressMessage( "ReSharper", "PartialTypeWithSinglePart", Justification = "Might break things." )]
	public partial class MainWindow : Window {
		private readonly TextBox output;
		private readonly TextBox gamePath;

		public MainWindow() {
			InitializeComponent();
			#if DEBUG
			this.AttachDevTools();
			#endif

			// Because of how Avalonia works, I have to do this to grab the controls.
			output   = this.FindControl<TextBox>( "DataOutput" );
			gamePath = this.FindControl<TextBox>( "GamePath" );

			// Sending this warning out, highly doubt anyone will see it.
			WriteLine( "Please make sure you back up your save before running this." ).GetAwaiter().GetResult();
		}

		private void InitializeComponent() {
			AvaloniaXamlLoader.Load( this );
		}

		[SuppressMessage( "ReSharper", "UnusedParameter.Local", Justification = "Breaks the axaml." )]
		private void InjectModLoader( object? sender, RoutedEventArgs e ) {
			// Since this method is only used by 1 button, which we also need a reference to, might as well just auto-cast is to a button.
			var button = ( Button )sender!;

			// Disables all of the user input into the game.
			button.IsEnabled   = false;
			gamePath.IsEnabled = false;

			// Don't block the main thread, so it will be ran on a background task.
			Task.Run( () => InjectModLoader( button ) );
		}

		private async Task InjectModLoader( InputElement button ) {
			/* Reason for not putting the entire thing up here is because Windows will behave differently to Linux (and macOS I assume).
			 * We want the exe to be ran using Mono instead of Wine, because if Wine tries to run it, it crashes. Mono is going to be better. */
			var injector = GetInjectorProgram( gamePath.Text );

			// Setting up the async receives since we want to output to the output on the UI.
			injector.OutputDataReceived += ReceiveOutputData;
			injector.ErrorDataReceived  += ReceiveOutputData;
			
			await WriteLine( "Proceeding to inject the mod loader. This can corrupt your game, make sure you can get a clean copy back." );
			await WriteLine( "Proceeding to launch the injection program now.\n" );

			// This section is starting the program, reading both standard out and error, and waiting for the program to close.
			injector.Start();
			injector.BeginOutputReadLine();
			injector.BeginErrorReadLine();
			injector.WaitForExit();

			// This section is just talking about how to install the mods.
			await WriteLine( $"Done injecting the mod loader. The mod's folder is located inside the games directory at `{Path.Combine( gamePath.Text, "Mods" )}.`" );
			await WriteLine( $"Mods need to be in their own folder, with the assemblies inside a folder called `Assemblies`." );
			await WriteLine( $"Happy modding!" );

			// Getting the main thread to reenable the user input controls.
			await Dispatcher.UIThread.InvokeAsync( () => button.IsEnabled   = true );
			await Dispatcher.UIThread.InvokeAsync( () => gamePath.IsEnabled = true );

			/* This method is to simple figure out what program needs to be executed for the best ability to run.
			 * If the program is running on Linux or macOS, then it needs to use Mono. Otherwise, if it is running
			 * on Windows, it can just directly launch the injector program without any fuss. */
			static Process GetInjectorProgram( string gamePath ) {
				var startInfo = new ProcessStartInfo( "", $"--no-interactive --force --game-location \"{gamePath}\"" ) {
					UseShellExecute        = false,
					RedirectStandardOutput = true,
					RedirectStandardError  = true,
					CreateNoWindow         = true,
					WorkingDirectory       = Environment.CurrentDirectory
				};

				if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ) {
					startInfo.FileName = "TwoCrownsInjector.exe";
				} else {
					startInfo.FileName  = $"mono";
					startInfo.Arguments = "TwoCrownsInjector.exe " + startInfo.Arguments;
				}

				return new Process() {
					StartInfo = startInfo
				};
			}

			void ReceiveOutputData( object sender, DataReceivedEventArgs args ) => WriteLine( args.Data );
		}

		/* This method writes out to the TextBox that is acting as a kind of "console".
		 * But because we do not know if we are on the main thread or not, it is going to
		 * use Tasks. First this method checks to see if it is on the main thread, and if
		 * not, will tell the dispatcher to invoke a method (which is basically a lambda
		 * back to the method itself with the same message). If it is on the main thread,
		 * it will output the message to the text box and proceed to the next line. It
		 * will then return the Task.CompletedTask to denote that it is done. */
		private Task WriteLine( string message = "\n" ) {
			if ( !Dispatcher.UIThread.CheckAccess() ) {
				return Dispatcher.UIThread.InvokeAsync( () => WriteLine( message ) );
			}

			output.Text += message + '\n';

			return Task.CompletedTask;
		}
	}
}
