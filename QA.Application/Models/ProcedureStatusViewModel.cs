namespace QA.API.Models
{
    public class ProcedureStatusViewModel
    {
        public int SerialNo { get; set; }
        public string ModuleName { get; set; } = string.Empty;
        public string InTime { get; set; } = string.Empty;
        public string? OutTime { get; set; } // Allow null for X icon
        public bool IsRunningToday { get; set; } // true = Working (both times)
        public string StatusText { get; set; } = string.Empty; // "Working" or "Not Working"
    }
}