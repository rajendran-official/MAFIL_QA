namespace QA.Application.Models
{
    public class VerificationItem
    {
        public string crfId { get; set; }
        public string subject { get; set; }
        public string releaseDate { get; set; }
        public string developer { get; set; }
        public string tester { get; set; }
        public string workingStatus { get; set; }  // null = Not Verified
    }
}

