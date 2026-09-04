namespace GrouchyFiler.Models
{
    public class RootConfig
    {
        public string Path { get; set; } = "";
        public List<PatternRule> Patterns { get; set; } = [];
        public List<string> Exclude { get; set; } = [];
        public bool IncludeSubdirectories { get; set; }
        public long MinimumAgeSeconds { get; set; } = 60;
        public long MinimumSizeBytes { get; set; }
        public long? MaximumSizeBytes { get; set; }
        public bool EmptyOnly { get; set; }
    }
}
