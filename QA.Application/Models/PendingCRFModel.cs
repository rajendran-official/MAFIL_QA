namespace QA.Application.Models
{
    public class PendingCRFModel
    {
        public string CRFID { get; set; } = string.Empty;
        public string REQUESTID { get; set; } = string.Empty;
        public string Objective { get; set; } = string.Empty;
        public DateTime? Testing_start_date { get; set; }
        public DateTime? Testing_end_date { get; set; }
        public string Developer { get; set; } = string.Empty;
        public string Techlead { get; set; } = string.Empty;
        public string STATUS { get; set; } = string.Empty;
    }
}
