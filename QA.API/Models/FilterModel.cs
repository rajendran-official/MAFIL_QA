using System.ComponentModel.DataAnnotations;

namespace QA.API.Models
{
    public class FilterModel
    {
        [Required]
        public string? fromDate { get; set; }
        [Required]
        public string? toDate { get; set; }
        public string? releaseType { get; set; }
        public string? testerTL { get; set; }
        public string? testerName { get; set; }
    }
}