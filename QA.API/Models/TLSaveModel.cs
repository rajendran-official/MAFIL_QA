namespace QA.API.Models
{
    public class TLSaveModel
    {
        public string crfId { get; set; } = "";
        public string? releaseDate { get; set; }
        public string? tlRemarks { get; set; }
        public string action { get; set; } = ""; // CONFIRM, RETURN, CLOSE
        public string? attachmentName { get; set; }
        public string? attachmentBase64 { get; set; }
        public string? attachmentMime { get; set; }
    }
}
