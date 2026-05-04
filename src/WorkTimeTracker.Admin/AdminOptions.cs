namespace WorkTimeTracker.Admin;

public class AdminStorageOptions
{
    public string ScreenshotsPath { get; set; } = @"C:\WorkTimeTracker\screenshots";
}

public class ActiveDirectoryOptions
{
    public string AdminGroup { get; set; } = "WORKTIME\\WorkTimeTracker-Admins";
}
