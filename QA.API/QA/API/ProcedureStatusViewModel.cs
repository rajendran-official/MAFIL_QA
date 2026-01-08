namespace QA.API
{
    internal class ProcedureStatusViewModel
    {
        public string ModuleName { get; internal set; }
        public string InTime { get; internal set; }
        public string? OutTime { get; internal set; }
        public bool IsRunningToday { get; internal set; }
        public string StatusText { get; internal set; }
    }
}