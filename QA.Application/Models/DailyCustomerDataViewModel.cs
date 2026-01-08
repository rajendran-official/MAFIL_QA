namespace QA.Application.Models
{
    public class DailyCustomerDataViewModel
    {
        public DateTime ReportDate { get; set; } = DateTime.Today;
        public int TotalCustomers { get; set; }
        public int HypervergeCustomers { get; set; }
        public int JukshioCustomers { get; set; }
        public int DoorstepCustomers { get; set; }
        public int AadharCount { get; set; }
        public int DigiCount { get; set; }
        public int OtherIdCount { get; set; }

       
    }
}