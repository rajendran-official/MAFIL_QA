namespace QA.Application.Models
{
    public class QueryViewModel
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public List<QueryItem> Results { get; set; } = new List<QueryItem>();

        // For adding new query
        public int? DepartmentId { get; set; }    // New
        public int? ModuleId { get; set; }        // New
        public string ControlName { get; set; }
        public string Query { get; set; }

        // Optional: Keep string versions if needed for display
        public string NewDepartment { get; set; }
        public string NewModule { get; set; }
        public int? FilterDepartmentId { get; set; }
        public int? FilterModuleId { get; set; }
    }
}
