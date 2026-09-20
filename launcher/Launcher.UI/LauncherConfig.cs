using System.IO;
using System.Reflection;
using Geumeum.Patch;

namespace Geumeum.Launcher;

/// 런처에 내장되는 신뢰 기준. 배포 주소와 서명 공개키는 원격에서 받지 않는다(설계서 6·10장).
public sealed class LauncherConfig
{
    public const string PLACEHOLDER_MARK = "CHANGE-ME";

    public string BaseUrl        { get; set; } = "";
    public string DefaultChannel { get; set; } = "internal";
    public Dictionary<string, string> TrustedKeys { get; set; } = new();

    public bool HasBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl) && !BaseUrl.Contains(PLACEHOLDER_MARK, StringComparison.Ordinal);

    public static LauncherConfig LoadEmbedded()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("launcher-config.json")
            ?? throw new InvalidOperationException("launcher-config.json이 런처에 포함되지 않았다");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        LauncherConfig config = StrictJson.Deserialize<LauncherConfig>(buffer.ToArray(), 64 * 1024);

        if (config.TrustedKeys.Count == 0)
        {
            throw new InvalidOperationException("런처에 신뢰할 서명 공개키가 없다");
        }
        return config;
    }
}

/// 사용자별 선택값. 설치 폴더 밖(%LOCALAPPDATA%\Geumeum)에 둔다.
public sealed class LauncherSettings
{
    public string InstallRoot { get; set; } = "";
    public string Channel     { get; set; } = "";

    /// 비어 있으면 런처에 내장된 주소를 쓴다. R2 전까지 팀장 PC 주소를 여기 넣는다
    public string ServerUrl   { get; set; } = "";

    public static string SettingsDir  => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Geumeum");
    public static string SettingsPath => Path.Combine(SettingsDir, "launcher-settings.json");
    public static string DefaultRoot  => Path.Combine(SettingsDir, "Game");

    public static LauncherSettings Load(string defaultChannel)
    {
        LauncherSettings? loaded = null;
        if (File.Exists(SettingsPath))
        {
            try
            {
                loaded = StrictJson.Deserialize<LauncherSettings>(File.ReadAllBytes(SettingsPath), 16 * 1024);
            }
            catch (PatchFormatException e)
            {
                System.Diagnostics.Trace.WriteLine($"[LauncherSettings] 설정 파일이 손상돼 기본값을 쓴다: {e.Message}");
            }
        }

        loaded ??= new LauncherSettings();
        if (string.IsNullOrWhiteSpace(loaded.InstallRoot)) loaded.InstallRoot = DefaultRoot;
        if (!RepoLayout.IsValidChannel(loaded.Channel))    loaded.Channel     = defaultChannel;
        loaded.ServerUrl ??= "";
        return loaded;
    }

    /// 배포 주소로 쓸 수 있는 http(s) 절대 주소인가. 내용 진위는 서명이 보장하므로 http도 받는다
    public static bool IsValidServerUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        string temp = SettingsPath + ".tmp";
        File.WriteAllBytes(temp, StrictJson.SerializeToUtf8Bytes(this));
        File.Move(temp, SettingsPath, true);
    }

    public bool MigrateLegacyServer(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? target)
            || target.Scheme != Uri.UriSchemeHttps
            || !Uri.TryCreate(ServerUrl, UriKind.Absolute, out Uri? current)
            || current.Scheme != Uri.UriSchemeHttp
            || current.Host != "169.212.47.146" || current.Port != 8765
            || current.AbsolutePath != "/" || current.Query.Length != 0 || current.Fragment.Length != 0)
            return false;

        ServerUrl = "";
        return true;
    }
}
