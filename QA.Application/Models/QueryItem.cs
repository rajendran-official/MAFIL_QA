namespace QA.Application.Models
{
    public class QueryItem
    {
        public int Id { get; set; }
        public string DepartmentName { get; set; }
        public string ModuleName { get; set; }
        public string ControlName { get; set; }
        public string QueryText { get; set; }
        public int RowCount { get; set; }
        public string Status => RowCount > 0 ? "Success" : "Failed";
        public string TechLeadName { get; set; } // Optional, populate if needed
        public string TesterTechLead { get; set; } // Optional
    }
}
