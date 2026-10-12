namespace DAGoldManager.Updates;

public static class UpdateBuildInfo
{
    public static string Version => typeof(UpdateBuildInfo).Assembly.GetName().Version!.ToString(3);
}
