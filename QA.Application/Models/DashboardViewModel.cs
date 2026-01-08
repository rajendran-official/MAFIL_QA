namespace QA.Application.Models
{
    public class DashboardViewModel
    {
        public string ReleaseVersion { get; set; } = "v2.4.1 - Live";
        public int PendingCRFCount { get; set; } = 18;
        public int TestCasesCount { get; set; } = 156;
        public int OpenBugsCount { get; set; } = 8;
    }
}
