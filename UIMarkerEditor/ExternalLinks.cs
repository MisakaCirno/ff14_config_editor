namespace UIMarkerEditor;

internal static class ExternalLinks
{
    public const string ServerListPage = "https://ff.web.sdo.com/web8/index.html#/servers";
    public const string ServerListReferer = "https://ff.web.sdo.com/";
    public const string ServerStatusApi = "https://ff14act.web.sdo.com/api/serverStatus/getServerStatus";
    public const string MapDataOnlineReferenceCsv = "https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/master/ContentFinderCondition.csv";
    public const string MapDataOnlineReferenceCommitApi = "https://api.github.com/repos/thewakingsands/ffxiv-datamining-cn/commits?path=ContentFinderCondition.csv&per_page=1";
    public const string MapDataDiemoeVersion = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/data.version";
    public const string MapDataDiemoeInstance = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/instance.json";

    public const string WayMarkSharePage = "https://souma.diemoe.net/ff14-overlay-vue/#/zoneMacro?OVERLAY_WS=ws://127.0.0.1:10501/ws&lang=zhCn";

    public static string CreateMapDataOnlineReferenceCsvUrl(string commitSha)
    {
        string normalizedCommitSha = commitSha.Trim();
        return string.IsNullOrWhiteSpace(normalizedCommitSha)
            ? MapDataOnlineReferenceCsv
            : $"https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/{normalizedCommitSha}/ContentFinderCondition.csv";
    }
}
