namespace QA.API.Models
{
    public class SaveModel
    {
        public required string crfId { get; set; }
        public required string requestId { get; set; }
        public string releaseDate { get; set; } = "";
        public required string workingStatus { get; set; }
        public string remarks { get; set; } = "";
        public string? attachmentName { get; set; }
        public string? attachmentBase64 { get; set; }
        public string? attachmentMime { get; set; }
    }
}
