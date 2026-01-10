using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Security.Claims;
using Newtonsoft.Json;

namespace QA.API.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class VerificationController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly string _connStr;
        private readonly ILogger<VerificationController> _logger;

        public VerificationController(IConfiguration config, ILogger<VerificationController> logger)
        {
            _config = config;
            _connStr = _config.GetConnectionString("OracleConnection")!;
            _logger = logger;
        }

        // =====================================================================
        // TESTER ENDPOINTS
        // =====================================================================

        // GET: api/Verification/tester
        [HttpGet("tester")]
        public async Task<IActionResult> GetTesterVerifications(
            [FromQuery] string fromDate,
            [FromQuery] string toDate,
            [FromQuery] string? releaseType = null)
        {
            var currentUser = GetCurrentUserName();
            if (currentUser == "Unknown User")
                return Unauthorized("User not identified");

            var list = new List<object>();

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("PROC_DAILY_VERIFICATION_MASTER", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "FETCH";
                cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = fromDate ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = toDate ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = releaseType ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = currentUser;
                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = DBNull.Value;
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                using var rdr = await cmd.ExecuteReaderAsync();

                while (await rdr.ReadAsync())
                {
                    list.Add(new
                    {
                        crfId = rdr["CRF_ID"]?.ToString() ?? "",
                        requestId = rdr["REQUEST_ID"]?.ToString() ?? "",
                        crfName = rdr["CRF_NAME"]?.ToString() ?? "",
                        releaseDate = rdr["RELEASE_DATE"]?.ToString() ?? "",
                        releaseType = rdr["RELEASE_TYPE"]?.ToString() ?? "",
                        techLeadName = rdr["TECHLEAD_NAME"]?.ToString() ?? "",
                        developerName = rdr["DEVELOPER_NAME"]?.ToString() ?? "",
                        testerTlName = rdr["TESTER_TL_NAME"]?.ToString() ?? "",
                        testerName = rdr["TESTER_NAME"]?.ToString() ?? "",
                        workingStatus = rdr["WORKING_STATUS"] != DBNull.Value ? Convert.ToInt32(rdr["WORKING_STATUS"]) : 0,
                        remarks = rdr["REMARKS"]?.ToString() ?? "",
                        tlRemarks = rdr["TL_REMARKS"]?.ToString() ?? "",
                        attachmentName = rdr["ATTACHMENT_NAME"]?.ToString() ?? "",
                        verifiedBy = rdr["VERIFIED_BY"]?.ToString() ?? "",
                        verifiedOn = rdr["VERIFIED_ON"]?.ToString() ?? "",
                        verifyStatus = rdr["VERIFY_STATUS"] != DBNull.Value ? Convert.ToInt32(rdr["VERIFY_STATUS"]) : 0
                    });
                }

                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tester verifications");
                return StatusCode(500, new { error = "Failed to load data", details = ex.Message });
            }
        }

        // POST: api/Verification/testersave
        [HttpPost("testersave")]
        public async Task<IActionResult> SaveTesterVerification([FromBody] List<TesterSaveModel> items)
        {
            if (items == null || items.Count == 0)
                return BadRequest("No data to save");

            var userName = GetCurrentUserName();

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("PROC_DAILY_VERIFICATION_MASTER", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "SAVE";
                cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = DBNull.Value;

                var jsonSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Include,
                    Formatting = Formatting.None
                };

                var jsonInput = JsonConvert.SerializeObject(items, jsonSettings);
                _logger.LogInformation("Tester Save JSON: {JsonInput}", jsonInput);

                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = jsonInput;
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = userName;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                using var rdr = await cmd.ExecuteReaderAsync();

                if (await rdr.ReadAsync())
                {
                    var status = rdr["status"]?.ToString();
                    var message = rdr["message"]?.ToString();

                    if (status == "SUCCESS")
                        return Ok(new { message });
                    else
                        return StatusCode(500, new { error = message });
                }

                return Ok(new { message = "Saved successfully" });
            }
            catch (OracleException oEx)
            {
                _logger.LogError(oEx, "Oracle error saving tester verification. Error Code: {ErrorCode}", oEx.Number);
                return StatusCode(500, new
                {
                    error = "Database error occurred",
                    details = oEx.Message,
                    errorCode = oEx.Number
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving tester verification");
                return StatusCode(500, new { error = "Save failed", details = ex.Message });
            }
        }

        // =====================================================================
        // TECH LEAD ENDPOINTS
        // =====================================================================

        // POST: api/Verification/tlview
        [HttpPost("tlview")]
        public async Task<IActionResult> GetTLVerifications([FromBody] TLViewRequest request)
        {
            var list = new List<object>();

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("PROC_DAILY_VERIFICATION_MASTER", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "TL_VERIFY";
                cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = request.FromDate ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = request.ToDate ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = request.ReleaseType ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = request.TesterName ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = DBNull.Value;
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                using var rdr = await cmd.ExecuteReaderAsync();

                while (await rdr.ReadAsync())
                {
                    list.Add(new
                    {
                        verifyId = rdr["VERIFY_ID"] != DBNull.Value ? Convert.ToInt32(rdr["VERIFY_ID"]) : 0,
                        crfId = rdr["CRF_ID"]?.ToString() ?? "",
                        requestId = rdr["REQUEST_ID"]?.ToString() ?? "",
                        crfName = rdr["CRF_NAME"]?.ToString() ?? "",
                        releaseDate = rdr["RELEASE_DATE"]?.ToString() ?? "",
                        releaseType = rdr["RELEASE_TYPE"]?.ToString() ?? "",
                        techLead = rdr["TECHLEAD_NAME"]?.ToString() ?? "",
                        developer = rdr["DEVELOPER_NAME"]?.ToString() ?? "",
                        testerTl = rdr["TESTER_TL_NAME"]?.ToString() ?? "",
                        tester = rdr["TESTER_NAME"]?.ToString() ?? "",
                        workingStatus = rdr["WORKING_STATUS"] != DBNull.Value ? Convert.ToInt32(rdr["WORKING_STATUS"]) : 0,
                        workingStatusText = rdr["WORKING_STATUS_TEXT"]?.ToString() ?? "",
                        remarks = rdr["REMARKS"]?.ToString() ?? "",
                        tlRemarks = rdr["TL_REMARKS"]?.ToString() ?? "",
                        attachmentName = rdr["ATTACHMENT_NAME"]?.ToString() ?? "",
                        verifiedBy = rdr["VERIFIED_BY"]?.ToString() ?? "",
                        verifiedOn = rdr["VERIFIED_ON"]?.ToString() ?? "",
                        verifyStatus = rdr["VERIFY_STATUS"] != DBNull.Value ? Convert.ToInt32(rdr["VERIFY_STATUS"]) : 0
                    });
                }

                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching TL verifications");
                return StatusCode(500, new { error = "Failed to load TL data", details = ex.Message });
            }
        }

        // POST: api/Verification/tlsave
        [HttpPost("tlsave")]
        public async Task<IActionResult> SaveTLAction([FromBody] List<TLActionModel> items)
        {
            if (items == null || items.Count == 0)
                return BadRequest("No data to save");

            var userName = GetCurrentUserName();

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("PROC_DAILY_VERIFICATION_MASTER", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "TL_SAVE";
                cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = DBNull.Value;

                var jsonSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Include,
                    Formatting = Formatting.None
                };

                var jsonInput = JsonConvert.SerializeObject(items, jsonSettings);
                _logger.LogInformation("TL Save JSON: {JsonInput}", jsonInput);

                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = jsonInput;
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = userName;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                using var rdr = await cmd.ExecuteReaderAsync();

                if (await rdr.ReadAsync())
                {
                    var status = rdr["status"]?.ToString();
                    var message = rdr["message"]?.ToString();

                    if (status == "SUCCESS")
                        return Ok(new { message });
                    else
                        return StatusCode(500, new { error = message });
                }

                return Ok(new { message = "TL action completed successfully" });
            }
            catch (OracleException oEx)
            {
                _logger.LogError(oEx, "Oracle error in TL action. Error Code: {ErrorCode}", oEx.Number);
                return StatusCode(500, new
                {
                    error = "Database error occurred",
                    details = oEx.Message,
                    errorCode = oEx.Number
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TL action");
                return StatusCode(500, new { error = "TL action failed", details = ex.Message });
            }
        }

        // =====================================================================
        // COMPLETED LIST ENDPOINT
        // =====================================================================

        // POST: api/Verification/completed
        [HttpPost("completed")]
        public async Task<IActionResult> GetCompletedList([FromBody] CompletedListRequest request)
        {
            var list = new List<object>();

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("PROC_DAILY_VERIFICATION_MASTER", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "COMPLETED_LIST";
                cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = request.FromDate ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = request.ToDate ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = request.ReleaseType ?? (object)DBNull.Value;
                cmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = DBNull.Value;
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                using var rdr = await cmd.ExecuteReaderAsync();

                while (await rdr.ReadAsync())
                {
                    list.Add(new
                    {
                        verifyId = rdr["VERIFY_ID"] != DBNull.Value ? Convert.ToInt32(rdr["VERIFY_ID"]) : 0,
                        crfId = rdr["CRF_ID"]?.ToString() ?? "",
                        requestId = rdr["REQUEST_ID"]?.ToString() ?? "",
                        crfName = rdr["CRF_NAME"]?.ToString() ?? "",
                        releaseDate = rdr["RELEASE_DATE"]?.ToString() ?? "",
                        releaseType = rdr["RELEASE_TYPE"]?.ToString() ?? "",
                        techLead = rdr["TECHLEAD_NAME"]?.ToString() ?? "",
                        developer = rdr["DEVELOPER_NAME"]?.ToString() ?? "",
                        testerTl = rdr["TESTER_TL_NAME"]?.ToString() ?? "",
                        tester = rdr["TESTER_NAME"]?.ToString() ?? "",
                        workingStatusText = rdr["WORKING_STATUS_TEXT"]?.ToString() ?? "",
                        remarks = rdr["REMARKS"]?.ToString() ?? "",
                        tlRemarks = rdr["TL_REMARKS"]?.ToString() ?? "",
                        attachmentName = rdr["ATTACHMENT_NAME"]?.ToString() ?? "",
                        verifiedBy = rdr["VERIFIED_BY"]?.ToString() ?? "",
                        verifiedOn = rdr["VERIFIED_ON"]?.ToString() ?? "",
                        approvedBy = rdr["APPROVED_BY"]?.ToString() ?? "",
                        approvedOn = rdr["APPROVED_ON"]?.ToString() ?? ""
                    });
                }

                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching completed list");
                return StatusCode(500, new { error = "Failed to load completed list", details = ex.Message });
            }
        }

        // =====================================================================
        // ATTACHMENT ENDPOINT
        // =====================================================================

        // GET: api/Verification/Attachment/{crfId}/{releaseDate}
        [HttpGet("Attachment/{crfId}/{releaseDate}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAttachment(string crfId, string releaseDate)
        {
            if (string.IsNullOrWhiteSpace(crfId) || string.IsNullOrWhiteSpace(releaseDate))
                return BadRequest("Invalid parameters");

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand(@"
                SELECT attachment, attachment_filename, attachment_mimetype
                FROM tbl_daily_release_verify
                WHERE UPPER(TRIM(crf_id)) = UPPER(TRIM(:crfId))
                  AND TO_CHAR(release_dt, 'DD-MON-YYYY') = UPPER(:releaseDate)", conn);

                cmd.Parameters.Add("crfId", OracleDbType.Varchar2).Value = crfId.Trim();
                cmd.Parameters.Add("releaseDate", OracleDbType.Varchar2).Value = releaseDate.Trim();

                using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var blob = reader["attachment"] as Oracle.ManagedDataAccess.Types.OracleBlob;
                    var fileName = reader["attachment_filename"]?.ToString() ?? "attachment";
                    var mimeType = reader["attachment_mimetype"]?.ToString() ?? "application/octet-stream";

                    if (blob == null || blob.IsNull || blob.Length == 0)
                    {
                        return Content($"<div style='padding:40px;text-align:center;'>" +
                                     $"<h3>No Attachment Found</h3>" +
                                     $"<p>CRF ID: {crfId}, Release Date: {releaseDate}</p></div>", "text/html");
                    }

                    var stream = new MemoryStream();
                    await blob.CopyToAsync(stream);
                    stream.Position = 0;

                    return File(stream, mimeType, fileName);
                }

                return NotFound("Attachment not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching attachment");
                return StatusCode(500, "Error retrieving attachment");
            }
        }

        private string GetCurrentUserName()
        {
            return User.FindFirst(ClaimTypes.GivenName)?.Value ??
                   User.FindFirst(ClaimTypes.Name)?.Value ??
                   User.FindFirst("unique_name")?.Value ??
                   "Unknown User";
        }
        [HttpGet("dashboardcounts")]
        public async Task<IActionResult> GetDashboardCounts()
        {
            var userName = GetCurrentUserName();

            if (userName == "Unknown User")
                return Unauthorized("User not identified");

            var techLeads = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "JIJIN E H", "MURUGESAN P", "NIKHIL SEKHAR", "SMINA BENNY", "VISAGH S", "JOBY JOSE"
    };

            bool isTechLead = techLeads.Contains(userName.Trim());

            int testerPendingCount = 0;
            int tlPendingCount = 0;
            int tlVerifiedCount = 0;
            int teamPendingCount = 0;
            int teamCompletedCount = 0;

            try
            {
                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                // 1. Tester Pending: All assigned released CRFs not yet TL-approved
                using (var cmd = new OracleCommand(@"
            SELECT COUNT(DISTINCT n.crf_id || TO_CHAR(n.updated_on, 'DD-MON-YYYY')) AS cnt
            FROM mana0809.srm_dailyrelease_updn n
            WHERE EXISTS (
                SELECT 1
                FROM mana0809.srm_test_assign ts
                JOIN mana0809.employee_master e ON TO_CHAR(ts.assign_to) = TO_CHAR(e.emp_code)
                WHERE TO_CHAR(ts.request_id) = TO_CHAR(n.request_id)
                  AND UPPER(e.emp_name) = UPPER(:p_tester_name)
                  AND e.status_id = 1
            )
            AND NOT EXISTS (
                SELECT 1
                FROM tbl_daily_release_verify v
                WHERE UPPER(TRIM(v.crf_id)) = UPPER(TRIM(n.crf_id))
                  AND TRUNC(v.release_dt) = TRUNC(n.updated_on)
                  AND v.status = 2  -- TL Approved
            )", conn))
                {
                    cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = userName;
                    var result = await cmd.ExecuteScalarAsync();
                    testerPendingCount = result != null ? Convert.ToInt32(result) : 0;
                }

                if (isTechLead)
                {
                    // 2. TL Pending: Tester verified/updated, awaiting TL action
                    using (var cmd = new OracleCommand(@"
                SELECT COUNT(*) AS cnt
                FROM tbl_daily_release_verify v
                WHERE UPPER(TRIM(v.techlead_name)) = UPPER(:p_tl_name)
                  AND v.status IN (1, 4)", conn))
                    {
                        cmd.Parameters.Add("p_tl_name", OracleDbType.Varchar2).Value = userName;
                        var result = await cmd.ExecuteScalarAsync();
                        tlPendingCount = result != null ? Convert.ToInt32(result) : 0;
                    }

                    // 3. TL Verified/Completed
                    using (var cmd = new OracleCommand(@"
                SELECT COUNT(*) AS cnt
                FROM tbl_daily_release_verify v
                WHERE UPPER(TRIM(v.techlead_name)) = UPPER(:p_tl_name)
                  AND v.status = 2", conn))
                    {
                        cmd.Parameters.Add("p_tl_name", OracleDbType.Varchar2).Value = userName;
                        var result = await cmd.ExecuteScalarAsync();
                        tlVerifiedCount = result != null ? Convert.ToInt32(result) : 0;
                    }

                    teamPendingCount = tlPendingCount;
                    teamCompletedCount = tlVerifiedCount;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetDashboardCounts for user {UserName}", userName);
                return StatusCode(500, new { error = "Failed to calculate dashboard counts", details = ex.Message });
            }

            return Ok(new
            {
                IsTechLead = isTechLead,
                TesterPendingCount = testerPendingCount,
                TlPendingCount = tlPendingCount,
                TlVerifiedCount = tlVerifiedCount,
                TeamPendingCount = teamPendingCount,
                TeamCompletedCount = teamCompletedCount
            });
        }
    }


    // =====================================================================
    // MODELS
    // =====================================================================

    public class TesterSaveModel
    {
        [JsonProperty("crfId")]
        public string CrfId { get; set; } = "";

        [JsonProperty("requestId")]
        public string? RequestId { get; set; }

        [JsonProperty("workingStatus")]
        public int WorkingStatus { get; set; }

        [JsonProperty("remarks")]
        public string Remarks { get; set; } = "";

        [JsonProperty("attachmentName")]
        public string? AttachmentName { get; set; }

        [JsonProperty("attachmentBase64")]
        public string? AttachmentBase64 { get; set; }

        [JsonProperty("attachmentMime")]
        public string? AttachmentMime { get; set; }
    }

    public class TLViewRequest
    {
        [JsonProperty("fromDate")]
        public string? FromDate { get; set; }

        [JsonProperty("toDate")]
        public string? ToDate { get; set; }

        [JsonProperty("releaseType")]
        public string? ReleaseType { get; set; }

        [JsonProperty("testerName")]
        public string? TesterName { get; set; }
    }

    public class TLActionModel
    {
        [JsonProperty("verifyId")]
        public int VerifyId { get; set; }

        [JsonProperty("crfId")]
        public string CrfId { get; set; } = "";

        [JsonProperty("releaseDate")]
        public string ReleaseDate { get; set; } = "";

        [JsonProperty("action")]
        public string Action { get; set; } = ""; // CONFIRM or RETURN

        [JsonProperty("tlRemarks")]
        public string TlRemarks { get; set; } = "";
    }

    public class CompletedListRequest
    {
        [JsonProperty("fromDate")]
        public string? FromDate { get; set; }

        [JsonProperty("toDate")]
        public string? ToDate { get; set; }

        [JsonProperty("releaseType")]
        public string? ReleaseType { get; set; }
    }
}