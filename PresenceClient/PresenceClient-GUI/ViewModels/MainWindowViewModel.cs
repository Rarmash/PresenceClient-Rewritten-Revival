using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using PresenceClient.Helpers;
using PresenceClient.Platform;
using PresenceClient.Views;
using PresenceCommon;
using ReactiveUI;

namespace PresenceClient.ViewModels
{
    public class MainWindowViewModel : ReactiveObject, IDisposable
    {
        private static TrayIconManager? trayIconManager;
        private bool autoConvertIpToMac;
        private string bigImageKey = "";
        private string bigImageText = "";
        private CancellationTokenSource? cancellationTokenSource;
        private string clientId = "";
        private bool displayHomeMenu = true;
        private bool hasSeenMacPrompt;
        private string ipAddress = "";
        private bool isConnected;
        private bool minimizeToTray;
        private Process? pythonBackendProcess;
        private IPAddress? resolvedIpAddress;
        private bool showTimeLapsed = true;
        private string smallImageKey = "";
        private string stateText = "";
        private string status = "";
        private bool usePythonBackend = true;
        private UserControl currentPage;
        private readonly string _configPath;

        public MainWindowViewModel()
        {
            _configPath = PlatformHelper.GetConfigPath();
            currentPage = new MainPage();

            if (PlatformHelper.CanUseTrayIcon())
                trayIconManager ??= new TrayIconManager(this);

            LoadConfig();

            var canConnect = this.WhenAnyValue(x => x.IsConnected).Select(connected => !connected);
            ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync, canConnect);

            var canDisconnect = this.WhenAnyValue(x => x.IsConnected);
            DisconnectCommand = ReactiveCommand.Create(Disconnect, canDisconnect);

            ReactiveCommand.Create(ShowMain);
            ReactiveCommand.Create(ShowSettings);
        }

        public ReactiveCommand<Unit, Unit> ConnectCommand { get; }
        public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }

        public UserControl CurrentPage
        {
            get => currentPage;
            set => this.RaiseAndSetIfChanged(ref currentPage, value);
        }

        public string IpAddress
        {
            get => ipAddress;
            set
            {
                this.RaiseAndSetIfChanged(ref ipAddress, value);
                SaveConfig();
            }
        }

        public string ClientId
        {
            get => clientId;
            set
            {
                this.RaiseAndSetIfChanged(ref clientId, value);
                SaveConfig();
            }
        }

        public string BigImageKey
        {
            get => bigImageKey;
            set
            {
                this.RaiseAndSetIfChanged(ref bigImageKey, value);
                SaveConfig();
            }
        }

        public string BigImageText
        {
            get => bigImageText;
            set
            {
                this.RaiseAndSetIfChanged(ref bigImageText, value);
                SaveConfig();
            }
        }

        public string SmallImageKey
        {
            get => smallImageKey;
            set
            {
                this.RaiseAndSetIfChanged(ref smallImageKey, value);
                SaveConfig();
            }
        }

        public string StateText
        {
            get => stateText;
            set
            {
                this.RaiseAndSetIfChanged(ref stateText, value);
                SaveConfig();
            }
        }

        public bool ShowTimeLapsed
        {
            get => showTimeLapsed;
            set
            {
                this.RaiseAndSetIfChanged(ref showTimeLapsed, value);
                SaveConfig();
            }
        }

        public bool MinimizeToTray
        {
            get => minimizeToTray;
            set
            {
                this.RaiseAndSetIfChanged(ref minimizeToTray, value);
                SaveConfig();
            }
        }

        public bool DisplayHomeMenu
        {
            get => displayHomeMenu;
            set
            {
                this.RaiseAndSetIfChanged(ref displayHomeMenu, value);
                SaveConfig();
            }
        }

        public bool AutoConvertIpToMac
        {
            get => autoConvertIpToMac;
            set
            {
                this.RaiseAndSetIfChanged(ref autoConvertIpToMac, value);
                SaveConfig();
            }
        }

        public bool UsePythonBackend
        {
            get => usePythonBackend;
            set
            {
                this.RaiseAndSetIfChanged(ref usePythonBackend, value);
                SaveConfig();
            }
        }

        public string Status
        {
            get => status;
            set => this.RaiseAndSetIfChanged(ref status, value);
        }

        public bool IsConnected
        {
            get => isConnected;
            set
            {
                this.RaiseAndSetIfChanged(ref isConnected, value);
                SaveConfig();
            }
        }

        public event EventHandler? ShowMainWindowRequested;

        public void ShowMainWindow()
        {
            ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleConnection()
        {
            if (IsConnected)
                Disconnect();
            else
                _ = ConnectAsync();
        }

        private async Task UpdateConnectionStatusAsync(bool isConnectedSecond)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsConnected = isConnectedSecond;
                trayIconManager?.UpdateIcon(isConnectedSecond);
            });
        }

        public void ExitApplication()
        {
            Disconnect();
            Environment.Exit(0);
        }

        public void Dispose()
        {
            Disconnect();
            if (trayIconManager != null)
            {
                trayIconManager.Dispose();
                trayIconManager = null;
            }
        }

        private void ShowMain()
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentPage = new MainPage
                {
                    DataContext = this
                };
            });
        }

        private void ShowSettings()
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentPage = new SettingsPage
                {
                    DataContext = this
                };
            });
        }

        private async Task ConnectAsync()
        {
            if (isConnected)
            {
                Disconnect();
                return;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                Status = "Client ID cannot be empty";
                return;
            }

            if (!UsePythonBackend)
            {
                Status = "Enable the Python backend in Settings.";
                return;
            }

            try
            {
                if (IPAddress.TryParse(ipAddress, out resolvedIpAddress))
                {
                    if (!hasSeenMacPrompt)
                    {
                        hasSeenMacPrompt = true;
                        autoConvertIpToMac = true;
                        await IpToMacAsync();
                    }
                    else if (autoConvertIpToMac)
                    {
                        await IpToMacAsync();
                    }
                }
                else
                {
                    var resolvedIp = NetworkUtils.GetIpByMac(ipAddress);
                    if (string.IsNullOrEmpty(resolvedIp))
                    {
                        Status = "Invalid IP or MAC Address";
                        return;
                    }

                    resolvedIpAddress = IPAddress.Parse(resolvedIp);
                }

                cancellationTokenSource = new CancellationTokenSource();
                isConnected = true;
                await RunPythonBackendAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Status = "Connection was cancelled";
            }
            catch (Exception ex)
            {
                Status = $"Connection error: {ex.Message}";
                isConnected = false;
            }
        }

        private void Disconnect()
        {
            try
            {
                cancellationTokenSource?.Cancel();

                if (pythonBackendProcess != null)
                {
                    if (!pythonBackendProcess.HasExited)
                        pythonBackendProcess.Kill(true);
                    pythonBackendProcess.Dispose();
                }

                pythonBackendProcess = null;
                isConnected = false;
                Status = "Disconnected";
                _ = UpdateConnectionStatusAsync(false);
            }
            catch (Exception ex)
            {
                Status = $"Error during disconnect: {ex.Message}";
            }
        }

        private async Task RunPythonBackendAsync(CancellationToken cancellationToken)
        {
            var backendCommand = await ResolvePythonBackendCommandAsync(cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = backendCommand.fileName,
                Arguments = backendCommand.arguments,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            pythonBackendProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            DebugLog.Log($"Starting Python backend: {startInfo.FileName} {startInfo.Arguments}");

            if (!pythonBackendProcess.Start())
                throw new InvalidOperationException("Python backend failed to start.");

            _ = Task.Run(() => PumpPythonOutputAsync(pythonBackendProcess, cancellationToken), cancellationToken);
            _ = Task.Run(() => PumpPythonErrorAsync(pythonBackendProcess, cancellationToken), cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() => { Status = "Python backend started."; });
            await UpdateConnectionStatusAsync(true);

            try
            {
                await pythonBackendProcess.WaitForExitAsync(cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Status = $"Python backend stopped with code {pythonBackendProcess.ExitCode}. See log: {DebugLog.GetLogPath()}";
                    });
                }
            }
            finally
            {
                pythonBackendProcess.Dispose();
                pythonBackendProcess = null;
                await UpdateConnectionStatusAsync(false);
            }
        }

        private async Task<(string fileName, string arguments)> ResolvePythonBackendCommandAsync(CancellationToken cancellationToken)
        {
            var bundledBackendPath = TryGetBundledPythonBackendPath();
            if (bundledBackendPath != null)
                return (bundledBackendPath, BuildPythonBackendRuntimeArguments());

            var pythonCommand = await ResolvePythonCommandAsync(cancellationToken);
            return (pythonCommand.fileName, BuildPythonScriptArguments(pythonCommand.prefixArguments));
        }

        private string? TryGetBundledPythonBackendPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "PresenceClient-Backend.exe"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "release", "PresenceClient-Backend.exe"))
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private async Task<(string fileName, string prefixArguments)> ResolvePythonCommandAsync(CancellationToken cancellationToken)
        {
            static async Task<bool> CanRunAsync(string fileName, string arguments, CancellationToken token)
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                if (!process.Start())
                    return false;

                await process.WaitForExitAsync(token);
                return process.ExitCode == 0;
            }

            if (await CanRunAsync("python", "--version", cancellationToken))
                return ("python", string.Empty);

            if (await CanRunAsync("py", "-3 --version", cancellationToken))
                return ("py", "-3 ");

            throw new InvalidOperationException("Python was not found. Install Python or add it to PATH.");
        }

        private string BuildPythonBackendRuntimeArguments()
        {
            var args = $"{resolvedIpAddress} {clientId} --debug";
            if (!DisplayHomeMenu)
                args += " --ignore-home-screen";
            return args;
        }

        private string BuildPythonScriptArguments(string prefixArguments)
        {
            var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PresenceClient-Py", "presence-client.py"));
            return $"{prefixArguments}\"{scriptPath}\" {BuildPythonBackendRuntimeArguments()}";
        }

        private async Task PumpPythonOutputAsync(Process process, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line == null)
                    break;

                DebugLog.Log($"[py] {line}");
                if (line.Contains("STATUS: ", StringComparison.Ordinal))
                {
                    var statusText = line[(line.IndexOf("STATUS: ", StringComparison.Ordinal) + "STATUS: ".Length)..];
                    await Dispatcher.UIThread.InvokeAsync(() => Status = statusText);
                }
                else if (line.Contains("GAME: ", StringComparison.Ordinal))
                {
                    var gameText = line[(line.IndexOf("GAME: ", StringComparison.Ordinal) + "GAME: ".Length)..];
                    await Dispatcher.UIThread.InvokeAsync(() => Status = $"Game: {gameText}");
                }
            }
        }

        private async Task PumpPythonErrorAsync(Process process, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line == null)
                    break;

                DebugLog.Log($"[py-err] {line}");
            }
        }

        private async Task IpToMacAsync()
        {
            if (resolvedIpAddress == null) return;

            var macAddress = NetworkUtils.GetMacByIp(resolvedIpAddress.ToString());
            if (!string.IsNullOrEmpty(macAddress))
            {
                ipAddress = macAddress;
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => Status = "Can't convert to MAC Address! Sorry!");
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath)) return;

                var jsonString = File.ReadAllText(_configPath);
                var cfg = JsonSerializer.Deserialize(jsonString, SourceGenerationContext.Default.Config);
                if (cfg == null) return;

                showTimeLapsed = cfg.DisplayTimer;
                bigImageKey = cfg.BigKey;
                bigImageText = cfg.BigText;
                smallImageKey = cfg.SmallKey;
                ipAddress = cfg.Ip;
                stateText = cfg.State;
                clientId = cfg.Client;
                minimizeToTray = cfg.AllowTray;
                displayHomeMenu = cfg.DisplayMainMenu;
                hasSeenMacPrompt = cfg.SeenAutoMacPrompt;
                autoConvertIpToMac = cfg.AutoToMac;
                usePythonBackend = cfg.UsePythonBackend;
                trayIconManager?.EnableTrayIcon(cfg.AllowTray);
            }
            catch (Exception ex)
            {
                Status = $"Error loading config: {ex.Message}";
                minimizeToTray = false;
            }
        }

        private void SaveConfig()
        {
            try
            {
                PlatformHelper.EnsureConfigDirectory();

                var cfg = new Config
                {
                    Ip = ipAddress,
                    Client = clientId,
                    BigKey = bigImageKey,
                    SmallKey = smallImageKey,
                    State = stateText,
                    BigText = bigImageText,
                    DisplayTimer = showTimeLapsed,
                    AllowTray = minimizeToTray,
                    DisplayMainMenu = displayHomeMenu,
                    SeenAutoMacPrompt = hasSeenMacPrompt,
                    AutoToMac = autoConvertIpToMac,
                    UsePythonBackend = usePythonBackend
                };

                var jsonString = JsonSerializer.Serialize(cfg, SourceGenerationContext.Default.Config);
                File.WriteAllText(_configPath, jsonString);
                trayIconManager?.EnableTrayIcon(minimizeToTray);
            }
            catch (Exception ex)
            {
                Status = $"Error saving config: {ex.Message}";
                Console.WriteLine("Error saving config: " + ex.Message);
            }
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(Config))]
    internal partial class SourceGenerationContext : JsonSerializerContext { }

    public class Config
    {
        public string Ip { get; set; } = "";
        public string Client { get; set; } = "";
        public string BigKey { get; set; } = "";
        public string SmallKey { get; set; } = "";
        public string State { get; set; } = "";
        public string BigText { get; set; } = "";
        public bool DisplayTimer { get; set; }
        public bool AllowTray { get; set; }
        public bool DisplayMainMenu { get; set; }
        public bool SeenAutoMacPrompt { get; set; }
        public bool AutoToMac { get; set; }
        public bool UsePythonBackend { get; set; }

        public Config()
        {
            DisplayMainMenu = true;
            DisplayTimer = true;
            UsePythonBackend = true;
        }
    }
}
