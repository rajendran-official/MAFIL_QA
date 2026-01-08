    namespace QA.API.Models 
    {
        public class DailyCustomerDataViewModel
        {
            public string ReportDate { get; set; } = DateTime.Today.ToString("dd-MMM-yyyy");

            public int TotalCustomers { get; set; }
            public int HypervergeCustomers { get; set; }
            public int JukshioCustomers { get; set; }
            public int DoorstepCustomers { get; set; }
            public int AadharCount { get; set; }
            public int DigiCount { get; set; }
            public int OtherIdCount { get; set; }
        }
    }