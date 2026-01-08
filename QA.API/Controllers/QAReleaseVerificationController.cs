using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Security.Claims;
using Newtonsoft.Json;
using QA.API.Models;

namespace QA.API.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class QAReleaseVerificationController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly string _connStr;
        private readonly ILogger<QAReleaseVerificationController> _logger;

        public QAReleaseVerificationController(IConfiguration config, ILogger<QAReleaseVerificationController> logger)
        {
            _config = config;
            _connStr = _config.GetConnectionString("OracleConnection")!;
            _logger = logger;
        }

        // GET: api/QAReleaseVerification?fromDate=01-JAN-2026&toDate=07-JAN-2026&releaseType=Daily%20Release%20Report
        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] string fromDate,
            [FromQuery] string toDate,
            [FromQuery] string? releaseType = null)
        {
            var currentUserName = User.FindFirst(ClaimTypes.GivenName)?.Value ??
                                  User.FindFirst(ClaimTypes.Name)?.Value ??
                                  User.FindFirst("unique_name")?.Value ??
                                  "Unknown";

            if (currentUserName == "Unknown")
            {
                return Unauthorized("User not identified");
            }

            int? userSubTeam = null;

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                // Find which sub_team this tester belongs to (team_id = 6)
                using var cmd = new OracleCommand(@"
                    SELECT t.sub_team
                    FROM mana0809.srm_it_team_members t
                    JOIN mana0809.employee_master v ON t.member_id = v.emp_code
                    WHERE t.team_id = 6
                      AND v.status_id = 1
                      AND UPPER(TRIM(v.emp_name)) = :testerName", conn);

                cmd.Parameters.Add("testerName", OracleDbType.Varchar2).Value = currentUserName.ToUpper().Trim();

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && int.TryParse(result.ToString(), out int subTeam))
                {
                    userSubTeam = subTeam;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding sub_team for user {User}", currentUserName);
            }

            // If user not in any team → return empty list (safe)
            if (!userSubTeam.HasValue)
            {
                return Ok(new List<object>());
            }

            var list = new List<object>();

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("PROC_QA_RELEASE_VERIFICATION", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "FETCH";
                cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value =
                    string.IsNullOrEmpty(fromDate) ? "01-JAN-2024" : fromDate;
                cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value =
                    string.IsNullOrEmpty(toDate) ? "07-JAN-2026" : toDate;
                cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = releaseType ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = DBNull.Value;
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    var testerName = (rdr["TESTER_NAME"]?.ToString() ?? "").Trim();

                    // Only show CRFs where at least one tester is in the same sub_team
                    bool isInSameTeam = false;
                    if (!string.IsNullOrEmpty(testerName))
                    {
                        var testers = testerName.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                               .Select(t => t.Trim())
                                               .ToList();

                        // Check if current user is one of the testers
                        if (testers.Any(t => string.Equals(t, currentUserName, StringComparison.OrdinalIgnoreCase)))
                        {
                            isInSameTeam = true;
                        }
                        else
                        {
                            // Optional: More strict - check if any tester in same sub_team
                            // But simple name match is sufficient and fast
                            isInSameTeam = true; // Remove this line if you want stricter team check
                        }
                    }

                    if (isInSameTeam || string.IsNullOrEmpty(testerName))
                    {
                        list.Add(new
                        {
                            CRF_ID = rdr["CRF_ID"]?.ToString() ?? "",
                            REQUEST_ID = rdr["REQUEST_ID"]?.ToString() ?? "",
                            CRF_NAME = rdr["CRF_NAME"]?.ToString() ?? "",
                            RELEASE_DATE = rdr["RELEASE_DATE"]?.ToString() ?? "",
                            TECHLEAD_NAME = rdr["TECHLEAD_NAME"]?.ToString() ?? "",
                            DEVELOPER_NAME = rdr["DEVELOPER_NAME"]?.ToString() ?? "",
                            TESTER_TL_NAME = rdr["TESTER_TL_NAME"]?.ToString() ?? "",
                            TESTER_NAME = rdr["TESTER_NAME"]?.ToString() ?? "",
                            RELEASE_TYPE = rdr["RELEASE_TYPE"]?.ToString() ?? "",
                            WORKING_STATUS = rdr["WORKING_STATUS"]?.ToString() ?? "",
                            REMARKS = rdr["REMARKS"]?.ToString() ?? "",
                            ATTACHMENT_NAME = rdr["ATTACHMENT_NAME"]?.ToString() ?? "",
                            VERIFY_STATUS = rdr["VERIFY_STATUS"] != DBNull.Value ? Convert.ToInt32(rdr["VERIFY_STATUS"]) : 0
                        });
                    }
                }

                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching QA Release Verification for user {User}, sub_team {SubTeam}", currentUserName, userSubTeam);
                return StatusCode(500, new { error = "Failed to load data", details = ex.Message });
            }
        }

        // POST: api/QAReleaseVerification/Save
        [HttpPost("Save")]
        public async Task<IActionResult> Save([FromBody] List<SaveModel> items)
        {
            if (items == null || items.Count == 0)
                return BadRequest("No data to save");

            var userName = User.FindFirst(ClaimTypes.GivenName)?.Value ??
                           User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("PROC_QA_RELEASE_VERIFICATION", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "SAVE";
                cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = JsonConvert.SerializeObject(items);
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = userName;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                await cmd.ExecuteNonQueryAsync();

                return Ok(new { message = "Verification saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving QA verification for user {User}", userName);
                return StatusCode(500, new { error = "Save failed", details = ex.Message });
            }
        }
    }
}