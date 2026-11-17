namespace SeatFlow.Core.Utilities;

/// <summary>
/// 应用程序环境信息帮助类。提供 exe 所在目录与数据目录等运行时路径。
/// </summary>
public static class AppEnvironment
{
    /// <summary>
    /// exe 文件所在目录。使用 <see cref="Environment.ProcessPath"/> 而非
    /// <see cref="AppContext.BaseDirectory"/>，因为单文件发布（PublishSingleFile=true）时
    /// 后者指向临时解压目录（如 %TEMP%\.net\...），导致数据被创建到错误位置。
    /// <see cref="Environment.ProcessPath"/> 在 .NET 6+ 的正常进程中始终非 null。
    /// </summary>
    public static string ExeDirectory => Path.GetDirectoryName(Environment.ProcessPath)!;

    /// <summary>
    /// 便携（portable）数据目录名：程序目录下的 <c>SeatFlowData</c> 文件夹，
    /// 随程序目录整体拷贝即可迁移，不写入系统 AppData / 用户主目录。
    /// </summary>
    public const string PortableDataFolderName = "SeatFlowData";

    /// <summary>
    /// 默认数据目录 —— 程序目录下的 <see cref="PortableDataFolderName"/> 文件夹
    /// （例：<c>D:\Apps\SeatFlow\SeatFlowData</c>）。用于存储
    /// Venues、Rosters、Snapshots、Logs、AppSettings.json 等全部用户数据。
    /// </summary>
    /// <remarks>
    /// 便携模式下数据与程序同目录。程序目录不可写（例如装在
    /// <c>Program Files</c> 下）时由 <see cref="ResolveDataDirectory"/> 回退到用户目录，
    /// 避免由于权限不足导致启动失败。
    /// </remarks>
    public static string DefaultDataDirectory => Path.Combine(ExeDirectory, PortableDataFolderName);

    /// <summary>
    /// 回退数据目录（仅当程序目录不可写时使用）：
    /// Windows <c>%LOCALAPPDATA%\SeatFlow</c> / Linux <c>~/.local/share/SeatFlow</c> /
    /// macOS <c>~/Library/Application Support/SeatFlow</c>。
    /// </summary>
    public static string FallbackDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeatFlow");

    /// <summary>
    /// 解析实际可用的数据目录：优先 <see cref="DefaultDataDirectory"/>（程序目录下），
    /// 若该位置无法创建/写入则回退到 <see cref="FallbackDataDirectory"/>。
    /// 创建目录时顺带写入/删除一个探针文件以验证可写性。
    /// </summary>
    public static string ResolveDataDirectory()
    {
        var portable = DefaultDataDirectory;
        if (TryEnsureWritable(portable))
            return portable;

        var fallback = FallbackDataDirectory;
        if (TryEnsureWritable(fallback))
            return fallback;

        // 两条路径都不可写时仍返回便携目录，让上层以既有方式报错（日志/启动检查）
        return portable;
    }

    /// <summary>创建目录并用探针文件验证可写性。</summary>
    private static bool TryEnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
