namespace Mods.ModManager
{
    /// <summary>A readable, nonempty UTF-8 document bundled with an installed mod.</summary>
    public sealed class ModDocument
    {
        public string Language { get; }
        public string Path { get; }
        public string Text { get; }

        public ModDocument(string language, string path, string text)
        {
            Language = language;
            Path = path;
            Text = text;
        }
    }
}
