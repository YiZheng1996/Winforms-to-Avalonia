namespace MainUI.Config
{
    internal static class AppInfo
    {
        public const string Name = "XXX试验台";

        public static string DataDirectory =>
            Path.Combine(Application.StartupPath, "data");

        public static string DatabasePath =>
            Path.Combine(DataDirectory, "XXX试验台.db");
    }
}
