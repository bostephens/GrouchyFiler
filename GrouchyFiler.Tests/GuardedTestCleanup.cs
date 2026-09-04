// Tests call the production deletion operation only after checking the test-data boundary.
internal sealed class GuardedTestCleanup
{
    private readonly string sandbox;

    public GuardedTestCleanup(string sandbox)
    {
        string testData = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data"));
        this.sandbox = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sandbox));
        if (!this.sandbox.StartsWith(testData + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Cleanup requires a test run folder below test-data.");
        RejectLinks(this.sandbox);
    }

    public void Delete(string path)
    {
        string target = ValidateTarget(path);
        GrouchyFiler.Services.FileCleanup.Delete(target);
    }

    internal string ValidateTarget(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new UnauthorizedAccessException("Cleanup requires an absolute filename.");
        string target = Path.GetFullPath(path);
        if (!target.StartsWith(sandbox + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Cleanup cannot leave this test run's folder.");
        RejectLinks(target);
        if ((File.GetAttributes(target) & FileAttributes.Directory) != 0)
            throw new UnauthorizedAccessException("Test cleanup deletes individual files only.");
        return target;
    }

    private static void RejectLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Test cleanup cannot follow symbolic links or junctions.");
    }
}
