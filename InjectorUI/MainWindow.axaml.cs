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

			output   = this.FindControl<TextBox>( "DataOutput" );
			gamePath = this.FindControl<TextBox>( "GamePath" );

			WriteLine( "Please make sure you back up your save before running this." ).GetAwaiter().GetResult();
		}

		private void InitializeComponent() {
			AvaloniaXamlLoader.Load( this );
		}

		[SuppressMessage( "ReSharper", "UnusedParameter.Local", Justification = "Breaks the axaml." )]
		private void InjectModLoader( object? sender, RoutedEventArgs e ) {
			var button = ( Button )sender!;

			button.IsEnabled   = false;
			gamePath.IsEnabled = false;

			Task.Run( () => InjectModLoader( button ) );
		}

		private async Task InjectModLoader( InputElement button ) {
			var injector = GetInjectorProgram( gamePath.Text );

			injector.OutputDataReceived += ReceiveOutputData;
			injector.ErrorDataReceived  += ReceiveOutputData;
			await WriteLine( "Proceeding to inject the mod loader. This can corrupt your game, make sure you can get a clean copy back." );
			await WriteLine( "Proceeding to launch the injection program now.\n" );

			injector.Start();
			injector.BeginOutputReadLine();
			injector.BeginErrorReadLine();
			injector.WaitForExit();

			await WriteLine( $"Done injecting the mod loader. The mod's folder is located inside the games directory at `{Path.Combine( gamePath.Text, "Mods" )}.`" );
			await WriteLine( $"Mods need to be in their own folder, with the assemblies inside a folder called `Assemblies`." );
			await WriteLine( $"Happy modding!" );

			await Dispatcher.UIThread.InvokeAsync( () => button.IsEnabled   = true );
			await Dispatcher.UIThread.InvokeAsync( () => gamePath.IsEnabled = true );

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

		private Task WriteLine( string message = "\n" ) {
			if ( !Dispatcher.UIThread.CheckAccess() ) {
				return Dispatcher.UIThread.InvokeAsync( () => WriteLine( message ) );
			}

			output.Text += message + '\n';

			return Task.CompletedTask;
		}
	}
}
