namespace QA.Application.Models
{
    public class KYCDeviationViewModel
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public List<KYCDeviationItem> Results { get; set; } = new List<KYCDeviationItem>();
    }
}
