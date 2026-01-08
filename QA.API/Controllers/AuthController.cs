using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace QA.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly string _oracleConnectionString;
        private readonly SymmetricSecurityKey _signingKey;
        private readonly string _pathBase;

        public AuthController(IConfiguration configuration, SymmetricSecurityKey signingKey)
        {
            _configuration = configuration;
            _oracleConnectionString = configuration.GetConnectionString("OracleConnection")
                ?? throw new InvalidOperationException("OracleConnection string missing.");

            _signingKey = signingKey;

            // For cookie Path in subpath deployment (e.g., /MAFIL_QA)
            _pathBase = configuration["AppSettings:PathBase"] ?? string.Empty;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.EmpCode) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { message = "EmpCode and Password are required." });

            await using var connection = new OracleConnection(_oracleConnectionString);
            try
            {
                await connection.OpenAsync();

                // Step 1: Validate credentials (emp_code + password)
                const string sql = @"
            SELECT emp_code, emp_name, PASSWORD
            FROM EMPLOYEE_MASTER
            WHERE emp_code = :EmpCode AND status_id = 1";

                await using var cmd = new OracleCommand(sql, connection);
                cmd.Parameters.Add("EmpCode", OracleDbType.Int32).Value = int.Parse(request.EmpCode);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Unauthorized(new { message = "Invalid EmpCode or Password." });

                int empCodeInt = reader.GetInt32("emp_code");
                string empName = reader.GetString("emp_name");
                string dbPassword = reader.GetString("PASSWORD");

                if (dbPassword != request.Password)
                    return Unauthorized(new { message = "Invalid EmpCode or Password." });

                // Step 2: NOW check if this user has QA module access
                await using var procCmd = new OracleCommand("PROC_DAILY_REPORTS_MASTER", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                procCmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "MODULE_ACCESS";
                procCmd.Parameters.Add("p_sub_flag", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = DBNull.Value;
                procCmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_emp_code", OracleDbType.Int32).Value = empCodeInt;
                procCmd.Parameters.Add("p_pageval", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_parval1", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                await using var procReader = await procCmd.ExecuteReaderAsync();
                string accessCode = "000";
                if (await procReader.ReadAsync())
                {
                    accessCode = procReader.GetString("access_code");
                }

                if (accessCode != "111")
                {
                    // This user exists and password is correct, but NOT authorized for QA portal
                    return Unauthorized(new { message = "You are not authorized to access the QA Portal." });
                }

                // Step 3: User is fully authorized → generate token
                string empCodeStr = empCodeInt.ToString();
                var token = GenerateJwtToken(empCodeStr, empName);
                Response.Cookies.Append("jwtToken", token, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.None,    // Required for cross-site cookies
                    Expires = DateTimeOffset.UtcNow.AddHours(8),
                    Path = "/",
                    Domain = "localhost"             // Share cookie across ports on localhost
                });

                return Ok(new
                {
                    token,
                    empName,
                    empCode = empCodeStr
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Login failed.", detail = ex.Message });
            }
        }

        [HttpPost("test-access")]
        [AllowAnonymous]
        public async Task<IActionResult> TestAccess([FromBody] TestAccessRequest request)
        {
            await using var connection = new OracleConnection(_oracleConnectionString);
            try
            {
                await connection.OpenAsync();
                int empCode = int.Parse(request.EmpCode);

                await using var procCmd = new OracleCommand("PROC_DAILY_REPORTS_MASTER", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                procCmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "MODULE_ACCESS";
                procCmd.Parameters.Add("p_sub_flag", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = DBNull.Value;
                procCmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_emp_code", OracleDbType.Int32).Value = empCode;
                procCmd.Parameters.Add("p_pageval", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_parval1", OracleDbType.Varchar2).Value = DBNull.Value;
                procCmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                await using var reader = await procCmd.ExecuteReaderAsync();
                string moduleAccessResult = "000";
                if (await reader.ReadAsync())
                {
                    moduleAccessResult = reader.GetString("access_code");
                }

                // Get employee info for debugging
                var empCmd = new OracleCommand(@"
                    SELECT emp_code, emp_name, department_id, status_id
                    FROM employee_master
                    WHERE emp_code = :EmpCode", connection);
                empCmd.Parameters.Add("EmpCode", OracleDbType.Int32).Value = empCode;

                object employeeInfo = new { Found = false };
                await using var empReader = await empCmd.ExecuteReaderAsync();
                if (await empReader.ReadAsync())
                {
                    employeeInfo = new
                    {
                        Found = true,
                        EmpCode = empReader.GetInt32("emp_code"),
                        EmpName = empReader.GetString("emp_name"),
                        DepartmentId = empReader.GetInt32("department_id"),
                        StatusId = empReader.GetInt32("status_id")
                    };
                }

                return Ok(new
                {
                    ModuleAccessResult = moduleAccessResult,
                    IsQAMember = moduleAccessResult == "111",
                    EmployeeInfo = employeeInfo
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stack = ex.StackTrace });
            }
        }

        private string GenerateJwtToken(string empCode, string empName)
        {
            var jwtSettings = _configuration.GetSection("Jwt");

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, empCode),
                new Claim(ClaimTypes.Name, empName),
                new Claim(ClaimTypes.GivenName, empName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(8),
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"],
                SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature)
            };

            var handler = new JwtSecurityTokenHandler();
            var token = handler.CreateToken(tokenDescriptor);
            return handler.WriteToken(token);
        }
    }

    // DTO Classes (must be inside namespace but outside controller)
    public class LoginRequest
    {
        public string EmpCode { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class TestAccessRequest
    {
        public string EmpCode { get; set; } = string.Empty;
    }
}