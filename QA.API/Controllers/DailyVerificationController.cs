using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System.Data;
using System.Security.Claims;

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

        // GET: api/DailyVerification?fromDate=09-01-2026&toDate=10-01-2026&releaseType=1
        [HttpGet]
        public async Task<IActionResult> GetVerifications(
            [FromQuery] string fromDate,
            [FromQuery] string toDate,
            [FromQuery] string? releaseType = "0")
        {
            try
            {
                _logger.LogInformation("=== API CALLED ===");
                _logger.LogInformation("FromDate: {FromDate}, ToDate: {ToDate}, ReleaseType: {ReleaseType}",
                    fromDate, toDate, releaseType);

                if (string.IsNullOrEmpty(fromDate) || string.IsNullOrEmpty(toDate))
                {
                    return BadRequest(new { error = "fromDate and toDate are required" });
                }

                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();
                _logger.LogInformation("Database connected");

                using var cmd = new OracleCommand("proc_qa_release_mst", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_pageval", OracleDbType.Varchar2).Value = "crf load";
                cmd.Parameters.Add("p_parval1", OracleDbType.Varchar2).Value = fromDate;
                cmd.Parameters.Add("p_parval2", OracleDbType.Varchar2).Value = toDate;
                cmd.Parameters.Add("p_parval3", OracleDbType.Varchar2).Value = releaseType ?? "0";

                var cursorParam = new OracleParameter("qry_result", OracleDbType.RefCursor)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(cursorParam);

                await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Stored procedure executed");

                var list = new List<Dictionary<string, object>>();

                if (cursorParam.Value == null || cursorParam.Value == DBNull.Value)
                {
                    _logger.LogWarning("RefCursor is null");
                    return Ok(new List<object>());
                }

                var refCursor = (OracleRefCursor)cursorParam.Value;
                using (var reader = refCursor.GetDataReader())
                {
                    _logger.LogInformation("Reading data, FieldCount: {Count}", reader.FieldCount);

                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string colName = reader.GetName(i);
                            object value = reader.IsDBNull(i) ? null! : reader.GetValue(i);
                            row[colName] = value!;
                        }
                        list.Add(row);
                    }
                }

                _logger.LogInformation("Retrieved {Count} records", list.Count);
                return Ok(list);
            }
            catch (OracleException oex)
            {
                _logger.LogError(oex, "Oracle error: {Message}, Code: {Code}", oex.Message, oex.Number);
                return StatusCode(500, new
                {
                    error = "Database error",
                    code = oex.Number,
                    message = oex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error: {Message}", ex.Message);
                return StatusCode(500, new
                {
                    error = "Internal error",
                    message = ex.Message
                });
            }
        }

        // POST: api/DailyVerification
        [HttpPost]
        public async Task<IActionResult> CreateVerification([FromForm] VerificationSubmitModel model)
        {
            try
            {
                var empCode = User.FindFirst("emp_code")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("EmployeeCode")?.Value
                    ?? "0";

                _logger.LogInformation("Tester {EmpCode} submitting verification for CRF {CrfId}",
                    empCode, model.CrfId);

                string? attachmentPath = null;
                if (model.Attachment != null && model.Attachment.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads", "qa_verifications");
                    Directory.CreateDirectory(uploadsFolder);

                    var fileName = $"{model.CrfId}_{model.RequestId}_{DateTime.Now:yyyyMMddHHmmss}_{model.Attachment.FileName}";
                    attachmentPath = Path.Combine(uploadsFolder, fileName);

                    using var stream = new FileStream(attachmentPath, FileMode.Create);
                    await model.Attachment.CopyToAsync(stream);
                }

                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("proc_qa_release_mst", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                var paramString = $"{model.CrfId}~{model.RequestId}~{model.WorkingStatus}~{model.Remarks}";

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_pageval", OracleDbType.Varchar2).Value = "insert";
                cmd.Parameters.Add("p_parval1", OracleDbType.Varchar2).Value = paramString;
                cmd.Parameters.Add("p_parval2", OracleDbType.Varchar2).Value = empCode;
                cmd.Parameters.Add("p_parval3", OracleDbType.Varchar2).Value = DBNull.Value;

                var cursorParam = new OracleParameter("qry_result", OracleDbType.RefCursor)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(cursorParam);

                await cmd.ExecuteNonQueryAsync();

                if (!string.IsNullOrEmpty(attachmentPath))
                {
                    await SaveAttachmentPath(conn, model.CrfId, model.RequestId, attachmentPath);
                }

                return Ok(new { success = true, message = "Verification submitted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating verification");
                return StatusCode(500, new { error = "Internal error", message = ex.Message });
            }
        }

        // PUT: api/DailyVerification
        [HttpPut]
        public async Task<IActionResult> UpdateVerification([FromBody] TLVerificationModel model)
        {
            try
            {
                var empCode = User.FindFirst("emp_code")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? "0";

                _logger.LogInformation("TL {EmpCode} updating verification for CRF {CrfId}",
                    empCode, model.CrfId);

                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("proc_qa_release_mst", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                var paramString = $"{model.CrfId}~{model.RequestId}~{model.WorkingStatus}~{model.Remarks}~{model.StatusId}~{empCode}";

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_pageval", OracleDbType.Varchar2).Value = "update";
                cmd.Parameters.Add("p_parval1", OracleDbType.Varchar2).Value = paramString;
                cmd.Parameters.Add("p_parval2", OracleDbType.Varchar2).Value = empCode;
                cmd.Parameters.Add("p_parval3", OracleDbType.Varchar2).Value = DBNull.Value;

                var cursorParam = new OracleParameter("qry_result", OracleDbType.RefCursor)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(cursorParam);

                await cmd.ExecuteNonQueryAsync();

                if (model.StatusId == 2)
                {
                    await UpdateReleaseStatus(conn, model.CrfId, model.RequestId, 16);
                }
                else if (model.StatusId == 3)
                {
                    await UpdateReleaseStatus(conn, model.CrfId, model.RequestId, 4);
                }

                return Ok(new { success = true, message = "Verification updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating verification");
                return StatusCode(500, new { error = "Internal error", message = ex.Message });
            }
        }

        // GET: api/DailyVerification/history?crfId=123&requestId=456
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory(
            [FromQuery] string crfId,
            [FromQuery] string requestId)
        {
            try
            {
                _logger.LogInformation("Getting history for CRF {CrfId}, Req {RequestId}", crfId, requestId);

                using var conn = new OracleConnection(_connStr);
                await conn.OpenAsync();

                using var cmd = new OracleCommand("proc_qa_release_mst", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_pageval", OracleDbType.Varchar2).Value = "history";
                cmd.Parameters.Add("p_parval1", OracleDbType.Varchar2).Value = crfId;
                cmd.Parameters.Add("p_parval2", OracleDbType.Varchar2).Value = requestId;
                cmd.Parameters.Add("p_parval3", OracleDbType.Varchar2).Value = DBNull.Value;

                var cursorParam = new OracleParameter("qry_result", OracleDbType.RefCursor)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(cursorParam);

                await cmd.ExecuteNonQueryAsync();

                var list = new List<Dictionary<string, object>>();
                var refCursor = (OracleRefCursor)cursorParam.Value;
                using (var reader = refCursor.GetDataReader())
                {
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string colName = reader.GetName(i);
                            object value = reader.IsDBNull(i) ? null! : reader.GetValue(i);
                            row[colName] = value!;
                        }
                        list.Add(row);
                    }
                }

                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting history");
                return StatusCode(500, new { error = "Internal error", message = ex.Message });
            }
        }

        private async Task SaveAttachmentPath(OracleConnection conn, string crfId, string requestId, string path)
        {
            var sql = @"UPDATE tbl_qa_rls_verify 
                       SET attachment_path = :path 
                       WHERE crf_id = :crfId AND request_id = :reqId";

            using var cmd = new OracleCommand(sql, conn);
            cmd.Parameters.Add("path", OracleDbType.Varchar2).Value = path;
            cmd.Parameters.Add("crfId", OracleDbType.Varchar2).Value = crfId;
            cmd.Parameters.Add("reqId", OracleDbType.Varchar2).Value = requestId;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task UpdateReleaseStatus(OracleConnection conn, string crfId, string requestId, int status)
        {
            var sql = @"UPDATE mana0809.srm_dailyrelease_updn 
                       SET status = :status, updated_on = SYSDATE 
                       WHERE crf_id = :crfId AND request_id = :reqId 
                       AND seq_rr = (SELECT MAX(seq_rr) FROM mana0809.srm_dailyrelease_updn 
                                     WHERE crf_id = :crfId AND request_id = :reqId)";

            using var cmd = new OracleCommand(sql, conn);
            cmd.Parameters.Add("status", OracleDbType.Int32).Value = status;
            cmd.Parameters.Add("crfId", OracleDbType.Varchar2).Value = crfId;
            cmd.Parameters.Add("reqId", OracleDbType.Varchar2).Value = requestId;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public class VerificationSubmitModel
    {
        public string CrfId { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public int WorkingStatus { get; set; }
        public string Remarks { get; set; } = string.Empty;
        public IFormFile? Attachment { get; set; }
    }

    public class TLVerificationModel
    {
        public string CrfId { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public int WorkingStatus { get; set; }
        public string Remarks { get; set; } = string.Empty;
        public int StatusId { get; set; }
    }
}