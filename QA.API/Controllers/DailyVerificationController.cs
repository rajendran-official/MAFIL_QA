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
    public class DailyVerificationController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly string _connStr;
        private readonly ILogger<DailyVerificationController> _logger;

        public DailyVerificationController(IConfiguration config, ILogger<DailyVerificationController> logger)
        {
            _config = config;
            _connStr = _config.GetConnectionString("OracleConnection")!;
            _logger = logger;
        }

        // GET: api/DailyVerification
        [HttpGet]
        public async Task<IActionResult> GetVerifications(
            [FromQuery] string fromDate,
            [FromQuery] string toDate,
            [FromQuery] string? releaseType = null,
            [FromQuery] string? testerName = null)
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
                cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value =
                    string.IsNullOrEmpty(testerName) ? currentUser : testerName;
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
                _logger.LogError(ex, "Error fetching verifications");
                return StatusCode(500, new { error = "Failed to load data", details = ex.Message });
            }
        }

        // POST: api/DailyVerification/Save
        [HttpPost("Save")]
        public async Task<IActionResult> SaveVerification([FromBody] List<VerificationSaveModel> items)
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

                // Ensure proper JSON serialization
                var jsonSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Include,
                    Formatting = Formatting.None
                };

                var jsonInput = JsonConvert.SerializeObject(items, jsonSettings);
                _logger.LogInformation("JSON Input: {JsonInput}", jsonInput);

                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = jsonInput;
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = userName;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                using var rdr = await cmd.ExecuteReaderAsync();

                if (await rdr.ReadAsync())
                {
                    var status = rdr["status"]?.ToString();
                    var message = rdr["message"]?.ToString();

                    _logger.LogInformation("Procedure response - Status: {Status}, Message: {Message}", status, message);

                    if (status == "SUCCESS")
                        return Ok(new { message });
                    else
                        return StatusCode(500, new { error = message });
                }

                return Ok(new { message = "Saved successfully" });
            }
            catch (OracleException oEx)
            {
                _logger.LogError(oEx, "Oracle error saving verification. Error Code: {ErrorCode}", oEx.Number);
                return StatusCode(500, new
                {
                    error = "Database error occurred",
                    details = oEx.Message,
                    errorCode = oEx.Number
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving verification");
                return StatusCode(500, new { error = "Save failed", details = ex.Message });
            }
        }
        // GET: api/DailyVerification/Attachment/{crfId}/{releaseDate}
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
                WHERE crf_id = :crfId
                  AND TO_CHAR(release_dt, 'DD-MON-YYYY') = :releaseDate", conn);

                cmd.Parameters.Add("crfId", OracleDbType.Varchar2).Value = crfId.Trim().ToUpper();
                cmd.Parameters.Add("releaseDate", OracleDbType.Varchar2).Value = releaseDate;

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
    }

    // Models
    public class VerificationSaveModel
    {
        [JsonProperty("crfId")]  // Add JSON property attributes
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
}