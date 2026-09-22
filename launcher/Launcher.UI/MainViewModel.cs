using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Geumeum.Patch;
using Geumeum.Patch.Client;
using Geumeum.Accounts;

namespace Geumeum.Launcher;

public enum LauncherState
{
    Checking,
    NotInstalled,
    UpdateAvailable,
    Installing,
    Paused,
    Ready,
    Offline,
    Running,
    LauncherOutdated,
    Error,
}

/// <summary>
/// 실행 흐름: 상태 복구 -> 업데이트 확인 -> 설치 계획 -> 다운로드 -> 검증 -> 적용 -> 실행
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly LauncherConfig   _config;
    private readonly LauncherSettings _settings;
    private readonly string?          _repoOverride;
    private readonly DispatcherTimer  _speedTimer;
    private readonly DispatcherTimer  _runningTimer;
    private readonly DispatcherTimer  _steamTimer;
    private SteamLoginState _steamLogin = SteamLoginState.Unknown;
    private readonly bool _gameAccounts;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _launching;

    private IRemoteRepository?       _remote;
    private Installer?               _installer;
    private InstallLayout            _layout;
    private PatchLog                 _log;
    private UpdateCheck?             _check;
    private LocalStatus?             _local;
    private CancellationTokenSource? _workCts;
    private bool                     _pauseRequested;
    private string                   _launcherWarning = "";

    private long   _downloaded;
    private long   _downloadTotal;
    private long   _lastDownloaded;
    private double _speed;

    private LauncherState _state = LauncherState.Checking;
    private string _statusText         = "준비 중";
    private string _detailText         = "";
    private string _currentVersionText = "-";
    private string _targetVersionText  = "-";
    private string _patchNotes         = "";
    private string _warningText        = "";
    private string _bytesText          = "";
    private string _speedText          = "";
    private double _progressValue;
    private bool   _progressVisible;

    public MainViewModel(LauncherConfig config, LauncherSettings settings, string? repoOverride, string? accountOverride = null, bool gameAccounts = false, string? accountStateDirectory = null)
    {
        _config       = config;
        _settings     = settings;
        _repoOverride = repoOverride;
        _gameAccounts = gameAccounts;
        _layout       = new InstallLayout(settings.InstallRoot);
        _log          = new PatchLog(_layout.LogsDir);

        Version assembly = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        LauncherVersion = new Version(assembly.Major, assembly.Minor, Math.Max(assembly.Build, 0));

        PrimaryCommand  = new RelayCommand(OnPrimaryAsync, () => IsPrimaryEnabled);
        SettingsCommand = new RelayCommand(OpenSettingsAsync,
            () => _state != LauncherState.Installing && !_launching && Account?.IsBusy != true);
        string accountOrigin = accountOverride ?? config.AccountApiUrl;
        if (!string.IsNullOrWhiteSpace(accountOrigin))
        {
            Account = new AccountViewModel(accountOrigin, accountOverride != null,
                accountStateDirectory ?? Path.Combine(LauncherSettings.SettingsDir, "Accounts"));
            Account.PropertyChanged += (_, _) =>
            {
                Raise(nameof(IsPrimaryEnabled));
                Raise(nameof(SteamLoginText));
                CommandManagerRefresh();
            };
        }
        AccountCommand = new RelayCommand(OpenAccountAsync,
            () => Account is { IsBusy: false } && State != LauncherState.Running && !_launching);

        _speedTimer   = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _speedTimer.Tick += (_, _) => UpdateSpeed();
        _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _runningTimer.Tick += async (_, _) => await PollRunningAsync();
        _steamTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _steamTimer.Tick += (_, _) => RefreshSteamLogin();
        RefreshSteamLogin();
        _steamTimer.Start();
    }

    /****************************************
    *              Properties
    ****************************************/

    public Version LauncherVersion { get; }
    public string  LauncherVersionText => $"런처 {LauncherVersion}";
    public string  ChannelText => _settings.Channel == "stable" ? "stable" : "internal (팀 테스트)";

    public RelayCommand PrimaryCommand  { get; }
    public RelayCommand SettingsCommand { get; }
    public AccountViewModel? Account { get; }
    public bool HasAccount => Account != null;
    public RelayCommand AccountCommand { get; }

    private Task OpenAccountAsync()
    {
        if (Account != null)
            new AccountWindow(Account) { Owner = Application.Current.MainWindow }.ShowDialog();
        return Task.CompletedTask;
    }
    public string SteamLoginText => _gameAccounts
        ? Account?.IsSignedIn == true ? "그믐 계정으로 접속합니다." : "계정 로그인 후 게임을 시작할 수 있습니다. 계정이 없으면 공식 Discord에서 발급받으세요."
        : _steamLogin switch
    {
        SteamLoginState.SignedIn => "Steam 로그인 감지됨",
        SteamLoginState.NotRunning => "Steam을 실행하고 로그인해 주세요.",
        SteamLoginState.SignedOut => "Steam에 로그인해 주세요. 로그인 후 게임을 시작할 수 있습니다.",
        _ => "Steam 로그인을 확인할 수 없습니다. Steam에서 로그인 상태를 확인해 주세요.",
    };
    public bool SteamLoginRequired => !_gameAccounts && _steamLogin != SteamLoginState.SignedIn;

    private void RefreshSteamLogin()
    {
        SteamLoginState next = SteamLoginStatus.Read();
        if (_steamLogin == next) return;
        _steamLogin = next;
        Raise(nameof(SteamLoginText));
        Raise(nameof(SteamLoginRequired));
        Raise(nameof(IsPrimaryEnabled));
        CommandManagerRefresh();
    }

    public LauncherState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(PrimaryButtonText));
                Raise(nameof(IsPrimaryEnabled));
                CommandManagerRefresh();
            }
        }
    }

    public string StatusText         { get => _statusText;         private set => Set(ref _statusText, value); }
    public string DetailText         { get => _detailText;         private set => Set(ref _detailText, value); }
    public string CurrentVersionText { get => _currentVersionText; private set => Set(ref _currentVersionText, value); }
    public string TargetVersionText  { get => _targetVersionText;  private set => Set(ref _targetVersionText, value); }
    public string PatchNotes         { get => _patchNotes;         private set => Set(ref _patchNotes, value); }
    public string WarningText        { get => _warningText;        private set => Set(ref _warningText, value); }
    public string BytesText          { get => _bytesText;          private set => Set(ref _bytesText, value); }
    public string SpeedText          { get => _speedText;          private set => Set(ref _speedText, value); }
    public double ProgressValue      { get => _progressValue;      private set => Set(ref _progressValue, value); }
    public bool   ProgressVisible    { get => _progressVisible;    private set => Set(ref _progressVisible, value); }

    public string PrimaryButtonText => _state switch
    {
        LauncherState.Checking         => "확인 중…",
        LauncherState.NotInstalled     => "설치",
        LauncherState.UpdateAvailable  => "업데이트",
        LauncherState.Installing       => "일시정지",
        LauncherState.Paused           => "계속",
        LauncherState.Ready            => _gameAccounts ? "원정대 찾기" : "게임 시작",
        LauncherState.Offline          => _gameAccounts ? "원정대 찾기" : "게임 시작",
        LauncherState.Running          => "실행 중",
        LauncherState.LauncherOutdated => "런처 받기",
        _                              => "다시 시도",
    };

    public bool IsPrimaryEnabled => !_launching
        && _state is not (LauncherState.Checking or LauncherState.Running)
        && (_state is not (LauncherState.Ready or LauncherState.Offline)
            || ((Account == null ? !_gameAccounts : Account.IsSignedIn && !Account.IsBusy)
                && !SteamLoginRequired));

    public string InstallRoot => _layout.Root;
    public string GameSizeText => _check == null ? "확인 중" : Format.Bytes(_check.Manifest.TotalSize);
    public string AvailableSpaceText
    {
        get
        {
            try
            {
                return Format.Bytes(new DriveInfo(Path.GetPathRoot(_layout.Root)!).AvailableFreeSpace);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _log.Warn($"디스크 여유 공간 조회 실패: {e.Message}");
                return "확인할 수 없음";
            }
        }
    }
    public string Channel     => _settings.Channel;
    public string LogsDir     => _layout.LogsDir;

    /// <summary>
    /// 설정에 적은 주소가 내장 주소보다 우선한다. 둘 다 없으면 빈 문자열
    /// </summary>
    public string ServerUrl => _repoOverride
        ?? (_settings.ServerUrl.Length > 0 ? _settings.ServerUrl : _config.HasBaseUrl ? _config.BaseUrl : "");

    public string ServerText => ServerUrl.Length > 0 ? ServerUrl : "설정되지 않음";

    /****************************************
    *                Flow
    ****************************************/

    public async Task InitializeAsync()
    {
        if (Account != null) await Account.RestoreAsync();
        RebuildServices();

        string? exePath = Environment.ProcessPath;
        if (exePath != null)
        {
            await LauncherUpdater.CleanupAsync(exePath, _log);
        }

        if (await TryUpdateLauncherAsync())
        {
            return;
        }
        await RefreshAsync();
    }

    /// <summary>
    /// 새 런처가 게시돼 있으면 받아서 교체하고 재시작한다. 재시작했으면 true - 호출자는 더 진행하지 않는다
    /// 실패해도 지금 런처로 계속 쓸 수 있게 경고만 남긴다
    /// </summary>
    private async Task<bool> TryUpdateLauncherAsync()
    {
#if DEBUG
        // 개발 빌드는 bin 폴더의 exe를 교체하지 않는다
        await Task.CompletedTask;
        return false;
#else
        string? exePath = Environment.ProcessPath;
        if (_remote == null || exePath == null)
        {
            return false;
        }

        State      = LauncherState.Checking;
        StatusText = "런처 업데이트 확인 중";
        DetailText = "";

        var updater = new LauncherUpdater(_remote, _config.TrustedKeys, LauncherVersion, _log);
        LauncherRelease? release;
        try
        {
            release = await Task.Run(() => updater.CheckAsync(CancellationToken.None));
        }
        catch (Exception e) when (e is RemoteTransientException or RemoteProtocolException or HttpRequestException)
        {
            // 서버에 못 붙으면 게임 확인 단계가 같은 이유로 안내한다
            _log.Warn($"런처 업데이트 확인 실패: {e.Message}");
            return false;
        }
        catch (Exception e) when (e is PatchTrustException or PatchFormatException)
        {
            _log.Error("런처 배포 정보 검증 실패", e);
            WarningText = _launcherWarning = "새 런처 정보의 서명이 맞지 않아 자동 업데이트를 건너뛰었다.";
            return false;
        }

        if (release == null)
        {
            return false;
        }

        StatusText      = $"런처 업데이트 중 ({LauncherVersion} → {release.Version})";
        ProgressVisible = true;
        ProgressValue   = 0;
        BytesText       = "";

        try
        {
            var progress = new Progress<long>(length =>
            {
                ProgressValue = 100.0 * length / release.Size;
                BytesText     = $"{Format.Bytes(length)} / {Format.Bytes(release.Size)}";
            });
            IProgress<long> report = progress;
            string newPath = await Task.Run(() => updater.DownloadAsync(release, exePath, report.Report, CancellationToken.None));

            LauncherUpdater.Swap(exePath, newPath);
            _log.Info($"런처 교체 완료: {LauncherVersion} → {release.Version}, 재시작한다");

            var restart = new ProcessStartInfo(exePath) { UseShellExecute = false };
            foreach (string argument in Environment.GetCommandLineArgs().Skip(1))
            {
                restart.ArgumentList.Add(argument);
            }
            Process.Start(restart)?.Dispose();
            Application.Current.Shutdown();
            return true;
        }
        catch (Exception e)
        {
            _log.Error("런처 자동 업데이트 실패", e);
            ProgressVisible = false;
            WarningText = _launcherWarning = $"런처 자동 업데이트에 실패했다({e.Message}). 지금 런처로 계속 쓸 수 있다.";
            return false;
        }
#endif
    }

    private void RebuildServices()
    {
        _remote?.Dispose();
        _remote    = null;
        _installer = null;
        _layout    = new InstallLayout(_settings.InstallRoot);
        _log       = new PatchLog(_layout.LogsDir);
        Raise(nameof(ChannelText));
        Raise(nameof(InstallRoot));
        Raise(nameof(ServerText));

        _remote = CreateRemote();
        if (_remote != null)
        {
            _installer = new Installer(_layout, _remote, _config.TrustedKeys, _settings.Channel, LauncherVersion, _log);
            _log.Info($"런처 {LauncherVersion} 시작: 저장소 {_remote.Description}, 채널 {_settings.Channel}, 설치 {_layout.Root}");
        }
    }

    private IRemoteRepository? CreateRemote()
    {
        string url = ServerUrl;
        if (url.Length == 0)
        {
            return null;
        }

        // 팀장 PC 서버는 http다. 받은 내용은 내장 공개키 서명과 파일 해시로 검증하므로 전송 암호화가 없어도 변조는 막힌다
        if (LauncherSettings.IsValidServerUrl(url))
        {
            return new HttpRemoteRepository(new Uri(url), allowInsecureHttp: true);
        }
        return _repoOverride != null ? new FolderRemoteRepository(_repoOverride) : null;
    }

    public async Task RefreshAsync()
    {
        ProgressVisible = false;
        _check = null;
        Raise(nameof(GameSizeText));
        Raise(nameof(AvailableSpaceText));
        BytesText       = "";
        SpeedText       = "";
        WarningText     = _launcherWarning;

        if (_installer == null)
        {
            Fail("업데이트 서버가 설정되지 않았습니다", "배포 담당자에게 최신 런처를 요청하세요.");
            return;
        }

        State      = LauncherState.Checking;
        StatusText = "업데이트 확인 중";
        DetailText = "";

        try
        {
            _local = await Task.Run(_installer.GetLocalStatus);
        }
        catch (Exception e)
        {
            HandleError(e, "설치 상태를 읽지 못했다");
            return;
        }

        CurrentVersionText = _local.Active == null
            ? "설치되지 않음"
            : _local.ActiveManifest?.DisplayVersion ?? "?";
        if (_local.Problem != null)
        {
            WarningText = $"현재 설치에 문제가 있다: {_local.Problem}";
        }

        try
        {
            _check = await Task.Run(() => _installer.CheckAsync(CancellationToken.None));
        }
        catch (Exception e)
        {
            HandleCheckError(e);
            return;
        }

        TargetVersionText = _check.Manifest.DisplayVersion;
        Raise(nameof(GameSizeText));
        PatchNotes = string.IsNullOrWhiteSpace(_check.Notes) ? "이 버전의 패치 노트가 없다." : _check.Notes;

        if (GameProcess.IsRunning(_layout))
        {
            EnterRunning();
        }
        else if (_check.IsUpToDate)
        {
            State      = LauncherState.Ready;
            StatusText = "최신 버전이다";
            DetailText = "";
        }
        else if (_local.IsInstalled)
        {
            State      = LauncherState.UpdateAvailable;
            StatusText = "새 버전이 있다";
            DetailText = $"전체 {Format.Bytes(_check.Manifest.TotalSize)} 중 바뀐 파일만 받는다";
        }
        else
        {
            State      = LauncherState.NotInstalled;
            StatusText = _local.Active == null ? "게임이 설치되지 않았다" : "설치가 손상돼 다시 받아야 한다";
            DetailText = $"설치 크기 {Format.Bytes(_check.Manifest.TotalSize)} · 위치 {_layout.Root}";
        }
    }

    private async Task OnPrimaryAsync()
    {
        if (!IsPrimaryEnabled) return;
        switch (_state)
        {
            case LauncherState.NotInstalled:
            case LauncherState.UpdateAvailable:
            case LauncherState.Paused:
                await InstallAsync();
                break;

            case LauncherState.Installing:
                _pauseRequested = true;
                _workCts?.Cancel();
                StatusText = "일시정지하는 중";
                break;

            case LauncherState.Ready:
            case LauncherState.Offline:
                if (Account != null)
                {
                    if (Account.IsBusy) return;
                    if (!Account.IsSignedIn) await OpenAccountAsync();
                    if (!Account.IsSignedIn) return;
                    await Account.RefreshProfileAsync();
                    if (Account.IsError || !Account.IsSignedIn)
                    {
                        WarningText = Account.Message;
                        return;
                    }
                }
                await LaunchAsync();
                break;

            case LauncherState.LauncherOutdated:
                // 자동 교체를 한 번 더 시도하고, 안 되면 브라우저로 받게 한다
                if (!await TryUpdateLauncherAsync())
                {
                    OpenUrl(ServerUrl.TrimEnd('/') + "/" + RepoLayout.LAUNCHER_KEY);
                    await RefreshAsync();
                }
                break;

            default:
                await RefreshAsync();
                break;
        }
    }

    private async Task InstallAsync()
    {
        if (_installer == null || _check == null)
        {
            await RefreshAsync();
            return;
        }

        UpdateCheck check = _check;
        await RunWorkAsync("설치", async (progress, ct) =>
        {
            await _installer.InstallAsync(check, progress, ct);
            _log.Info($"설치 완료: {check.Manifest.ReleaseId}");
        });
    }

    public async Task RepairAsync()
    {
        if (_installer == null)
        {
            return;
        }

        RepairResult? result = null;
        await RunWorkAsync("파일 검사·복구", async (progress, ct) =>
        {
            result = await _installer.VerifyAndRepairAsync(progress, ct);
        });

        if (result != null)
        {
            MessageBox.Show($"파일 {result.Checked}개를 검사했다.\n복구 {result.Repaired}개 · 불필요한 실행 파일 제거 {result.RemovedExtras}개",
                "파일 검사·복구", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // 설치와 복구가 같은 진행 화면, 일시정지, 오류 처리를 쓴다
    private async Task RunWorkAsync(string title, Func<IProgress<InstallProgress>, CancellationToken, Task> work)
    {
        State           = LauncherState.Installing;
        StatusText      = title + " 준비 중";
        DetailText      = "";
        WarningText     = "";
        ProgressVisible = true;
        ProgressValue   = 0;
        _pauseRequested = false;
        _downloaded     = _lastDownloaded = _downloadTotal = 0;
        _speed          = 0;
        _workCts        = new CancellationTokenSource();
        _speedTimer.Start();

        var progress = new Progress<InstallProgress>(OnProgress);
        CancellationToken token = _workCts.Token;

        try
        {
            await Task.Run(() => work(progress, token), token);
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (_pauseRequested)
        {
            State      = LauncherState.Paused;
            StatusText = "일시정지됨";
            DetailText = "받은 부분은 보존된다. 계속을 누르면 이어서 받는다.";
            SpeedText  = "";
        }
        catch (Exception e)
        {
            HandleInstallError(e);
        }
        finally
        {
            _speedTimer.Stop();
            _workCts.Dispose();
            _workCts = null;
        }
    }

    private void OnProgress(InstallProgress p)
    {
        StatusText = p.Stage switch
        {
            InstallStage.Copying     => "기존 파일 검사·복사 중",
            InstallStage.Downloading => "다운로드 중",
            InstallStage.Verifying   => "파일 검사 중",
            InstallStage.Finalizing  => "적용 중",
            InstallStage.Repairing   => "손상 파일 다시 받는 중",
            _                        => "준비 중",
        };

        ProgressValue = p.TotalBytes > 0 ? 100.0 * p.DoneBytes / p.TotalBytes : 0;
        _downloaded    = p.DownloadedBytes;
        _downloadTotal = p.DownloadTotalBytes;

        BytesText = p.Stage is InstallStage.Downloading or InstallStage.Repairing
            ? $"{Format.Bytes(p.DownloadedBytes)} / {Format.Bytes(p.DownloadTotalBytes)} 받음"
            : $"{Format.Bytes(p.DoneBytes)} / {Format.Bytes(p.TotalBytes)}";
    }

    private void UpdateSpeed()
    {
        long delta = _downloaded - _lastDownloaded;
        _lastDownloaded = _downloaded;
        if (delta < 0)
        {
            return;
        }

        _speed = _speed <= 0 ? delta : (_speed * 0.7) + (delta * 0.3);
        if (_speed < 1)
        {
            SpeedText = "";
            return;
        }

        long remaining = Math.Max(0, _downloadTotal - _downloaded);
        TimeSpan eta = TimeSpan.FromSeconds(remaining / _speed);
        SpeedText = $"{Format.Bytes((long)_speed)}/s · 남은 시간 {(eta.TotalHours >= 1 ? eta.ToString(@"h\:mm\:ss") : eta.ToString(@"m\:ss"))}";
    }

    private async Task LaunchAsync()
    {
        if (_local?.Active == null || _local.ActiveManifest == null || !_local.IsInstalled)
        {
            Fail("실행할 설치가 없다", _local?.Problem ?? "먼저 설치한다.");
            return;
        }

        // 실행 직전에 다시 확인한다. 로그인 대기 때문에 Ready/Offline 상태를 잃지 않는다
        RefreshSteamLogin();
        if (!_gameAccounts && SteamLoginRequired)
        {
            MessageBox.Show(SteamLoginText, "Steam 로그인 필요", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RotateGameLog();
        Process? launched = null;
        TokenReply? gameSession = null;
        _launching = true;
        Raise(nameof(IsPrimaryEnabled));
        CommandManagerRefresh();
        try
        {
            AccountRoom? room = null;
            if (_gameAccounts)
            {
                if (Account?.IsSignedIn != true) throw new AccountException("먼저 계정에 로그인해 주세요.");
                var rooms = new RoomWindow(Account, _local.Active.ReleaseId) { Owner = Application.Current.MainWindow };
                if (rooms.ShowDialog() != true) return;
                room = rooms.SelectedRoom;
            }
            using var pipe = _gameAccounts ? new GameSessionPipe() : null;
            var arguments = GameArguments().ToList();
            if (pipe != null)
            {
                gameSession = await Account!.CreateGameSessionAsync(_lifetime.Token);
                arguments.AddRange(new[] { "-account-pipe", pipe.Name, "-launcher-pid", Environment.ProcessId.ToString() });
            }
            Process process = GameProcess.Launch(_layout, _local.Active.ReleaseId, _local.ActiveManifest, arguments);
            launched = process;
            _log.Info($"게임 실행: {_local.Active.ReleaseId} (pid {process.Id})");
            if (pipe != null)
            {
                await pipe.DeliverAsync(process.Id, new Uri(Account!.ApiOrigin), gameSession!, _lifetime.Token, room);
                _log.Info("게임 계정 세션 전달 확인");
            }
            process.Exited += (_, _) => Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                string exitCode = SafeExitCode(process);
                _log.Info($"게임 종료: 코드 {exitCode}");
                process.Dispose();
                await RefreshAsync();
                if (exitCode == "12") WarningText = "게임 계정 세션이 종료되었습니다. 계정 서버를 확인하고 다시 로그인해 주세요.";
                if (exitCode == "13") WarningText = "파티 연결이 종료되었습니다. 인터넷 연결을 확인한 뒤 파티를 다시 만들어 주세요.";
            });
            process.EnableRaisingEvents = true;
            EnterRunning();
        }
        catch (Exception e)
        {
            if (launched != null)
            {
                try { if (!launched.HasExited) launched.Kill(); }
                catch (Exception stopError) when (stopError is InvalidOperationException or System.ComponentModel.Win32Exception)
                { _log.Warn("실패한 게임 프로세스 종료를 확인하지 못했습니다."); }
                launched.Dispose();
            }
            if (gameSession != null)
            {
                try
                {
                    using var api = new AccountApi(Account!.ApiOrigin, true);
                    await api.LogoutAsync(gameSession.AccessToken);
                }
                catch (AccountException) { _log.Warn("실패한 게임 세션의 원격 폐기를 확인하지 못했습니다."); }
            }
            HandleError(new AccountException(e is AccountException ? e.Message : "게임 실행 또는 계정 세션 전달에 실패했습니다."), "게임을 시작하지 못했다");
        }
        finally { _launching = false; Raise(nameof(IsPrimaryEnabled)); CommandManagerRefresh(); }
    }

    // 런처로 켜면 항상 Steam 로비 경로로 들어간다(tools/run-steam-lobby.bat과 같은 경로). 게임 로그는 로그 폴더에 같이 둔다
    private IReadOnlyList<string> GameArguments()
    {
        return _gameAccounts
            ? new[] { "-logFile", Path.Combine(_layout.LogsDir, "game.log") }
            : new[] { "-steam", "-logFile", Path.Combine(_layout.LogsDir, "game.log") };
    }

    // 직전 실행 로그 하나는 남긴다. 튕긴 뒤 다시 켜도 원인 로그가 덮이지 않게 한다
    private void RotateGameLog()
    {
        string current  = Path.Combine(_layout.LogsDir, "game.log");
        string previous = Path.Combine(_layout.LogsDir, "game-previous.log");
        try
        {
            Directory.CreateDirectory(_layout.LogsDir);
            if (File.Exists(current))
            {
                File.Move(current, previous, true);
            }
        }
        catch (IOException e)
        {
            _log.Warn($"게임 로그 정리 실패: {e.Message}");
        }
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString();
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }

    private void EnterRunning()
    {
        State      = LauncherState.Running;
        StatusText = "게임 실행 중";
        DetailText = "게임을 끄면 다시 업데이트를 확인한다.";
        _runningTimer.Start();
    }

    // 런처 밖에서 켠 게임은 프로세스 핸들이 없어서 주기적으로 본다
    private async Task PollRunningAsync()
    {
        if (_state != LauncherState.Running)
        {
            _runningTimer.Stop();
            return;
        }
        if (!GameProcess.IsRunning(_layout))
        {
            _runningTimer.Stop();
            await RefreshAsync();
        }
    }

    private async Task OpenSettingsAsync()
    {
        var window = new SettingsWindow(_settings.InstallRoot, _settings.Channel, _settings.ServerUrl,
            _config.HasBaseUrl ? _config.BaseUrl : "", _layout.LogsDir) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true)
        {
            return;
        }

        bool changed = !string.Equals(window.InstallRoot, _settings.InstallRoot, StringComparison.OrdinalIgnoreCase)
                    || window.Channel != _settings.Channel
                    || window.ServerUrl != _settings.ServerUrl;
        if (changed)
        {
            _settings.InstallRoot = window.InstallRoot;
            _settings.Channel     = window.Channel;
            _settings.ServerUrl   = window.ServerUrl;
            _settings.Save();
            RebuildServices();
            Raise(nameof(InstallRoot));
            Raise(nameof(Channel));
            Raise(nameof(LogsDir));
        }

        if (window.RepairRequested)
        {
            await RepairAsync();
        }
        else if (changed)
        {
            await RefreshAsync();
        }
    }

    /****************************************
    *              Error Text
    ****************************************/

    private void HandleCheckError(Exception e)
    {
        switch (e)
        {
            case LauncherTooOldException tooOld:
                _log.Warn(tooOld.Message);
                State      = LauncherState.LauncherOutdated;
                StatusText = "런처를 새로 받아야 한다";
                DetailText = $"이 배포는 런처 {tooOld.Required} 이상이 필요하다. 현재 {LauncherVersion}.";
                return;

            case RemoteNotFoundException:
                Fail($"{_settings.Channel} 채널에 게시된 배포가 없다", "배포 담당자에게 게시 여부를 확인한다.");
                _log.Warn(e.Message);
                return;

            case RemoteTransientException or RemoteProtocolException or HttpRequestException:
                _log.Warn($"업데이트 확인 실패: {e.Message}");
                if (_local?.IsInstalled == true)
                {
                    State       = LauncherState.Offline;
                    StatusText  = "서버에 연결하지 못했다";
                    DetailText  = "마지막으로 설치한 버전을 실행할 수 있다.";
                    WarningText = "최신 버전인지 확인하지 못했다. 다른 사람과 버전이 다르면 접속이 거부된다.";
                }
                else
                {
                    Fail("서버에 연결하지 못했다", "인터넷 연결을 확인하고 다시 시도한다.");
                }
                return;

            case PatchClockException:
                _log.Warn(e.Message);
                Fail("PC 시간이 맞지 않는다", "Windows 설정 → 시간 → 지금 동기화 후 다시 시도한다.");
                return;

            case PatchTrustException or PatchFormatException:
                _log.Error("배포 정보 검증 실패", e);
                Fail("배포 정보 검증에 실패해 설치를 막았다", e.Message);
                return;

            default:
                HandleError(e, "업데이트를 확인하지 못했다");
                return;
        }
    }

    private void HandleInstallError(Exception e)
    {
        switch (e)
        {
            case GameRunningException:
                Fail("게임이 실행 중이다", "게임을 끄고 다시 시도한다.");
                return;

            case DiskSpaceException disk:
                Fail("디스크 공간이 부족하다", $"{Format.Bytes(disk.Needed - disk.Available)} 이상 확보하거나 설정에서 설치 위치를 바꾼다.");
                return;

            case RemoteTransientException:
                _log.Warn($"설치 중 네트워크 오류: {e.Message}");
                State      = LauncherState.Paused;
                StatusText = "네트워크 오류로 멈췄다";
                DetailText = "받은 부분은 보존됐다. 계속을 누르면 이어서 받는다.";
                SpeedText  = "";
                return;

            case UnauthorizedAccessException or IOException:
                _log.Error("설치 파일 접근 실패", e);
                Fail("파일을 쓰지 못했다", $"{e.Message}\n백신이 파일을 격리했거나 다른 프로그램이 잡고 있을 수 있다. 백신을 끄지 말고 해당 파일을 예외로 두거나 다시 시도한다.");
                return;

            default:
                HandleCheckError(e);
                return;
        }
    }

    private void HandleError(Exception e, string title)
    {
        if (e is InstallBusyException)
        {
            Fail("다른 런처가 설치 중이다", "다른 런처 창을 닫고 다시 시도한다.");
            return;
        }
        if (e is InstallBrokenException)
        {
            Fail(title, e.Message);
            return;
        }

        _log.Error(title, e);
        Fail(title, $"{e.Message}\n자세한 내용은 설정 → 로그 폴더 열기.");
    }

    private void Fail(string status, string detail)
    {
        State           = LauncherState.Error;
        StatusText      = status;
        DetailText      = detail;
        ProgressVisible = false;
        SpeedText       = "";
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    }

    private static void CommandManagerRefresh()
    {
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// 창을 닫으면 진행 중인 작업을 멈춘다. 받은 부분은 다음 실행에 이어 쓴다
    /// </summary>
    public void CancelWork()
    {
        _pauseRequested = true;
        _workCts?.Cancel();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _speedTimer.Stop();
        _runningTimer.Stop();
        _steamTimer.Stop();
        Account?.Dispose();
        _remote?.Dispose();
    }
}
