namespace MainGameKaleidoscope
{
    public static class KaleidoscopeApi
    {
        public static bool IsAvailable => Plugin.Instance != null;
        public static void SetThoughtActive(bool active) => Plugin.Instance?.SetThoughtActive(active);
    }
}
