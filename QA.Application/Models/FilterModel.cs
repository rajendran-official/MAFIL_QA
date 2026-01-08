using System.ComponentModel.DataAnnotations;

namespace QA.Application.Models
{
    public class FilterModel
    {
        public string? fromDate { get; set; }
        [Required]
        public string? toDate { get; set; }
        public string? releaseType { get; set; }
        public string? testerTL { get; set; }
        public string? testerName { get; set; }
    }
}
