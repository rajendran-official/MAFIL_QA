using System.ComponentModel.DataAnnotations;

namespace QA.Application.Models
{

    public class LoginModel
    {
        [Required]
        public string EmpCode { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }

}
