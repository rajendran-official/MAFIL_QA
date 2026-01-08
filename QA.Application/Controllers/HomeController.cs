using Microsoft.AspNetCore.Authorization;  // ← Important
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Diagnostics;
using QA.Application.Models;
using System.Security.Claims;

namespace QA.Application.Controllers
{
    [Authorize]  
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _config;
        private readonly string _connStr;

        public HomeController(ILogger<HomeController> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
            _connStr = _config.GetConnectionString("OracleConnection")
                ?? throw new InvalidOperationException("OracleConnection string is missing.");
        }

        // Public pages - explicitly allow anonymous
        [AllowAnonymous]
        public IActionResult Index() => RedirectToAction("Dashboard");

        [AllowAnonymous]
        public IActionResult Login() => View();

        [AllowAnonymous]
        public IActionResult Privacy() => View();

        // Protected pages - automatically protected by [Authorize] on class
        public IActionResult Dashboard() => View();

        public IActionResult DailyVerification() => View();

        public IActionResult ReleaseStatus()
        {
            ViewBag.TesterTLs = GetTesterTLs();
            return View();
        }

        public IActionResult CRFList()
        {
            return View(); 
        }

        public IActionResult QAReleaseVerification()
        {
            return View();
        }

        public IActionResult DailyReleaseVerificationNew()
        {
            ViewData["Title"] = "Daily Release Verification new";
            return View("dailyreleaseverificationnew"); // Explicit view name (case-insensitive)
        }
        public IActionResult DailyVerificationTL() => View();

        public IActionResult QueryStatus(int? FilterDepartmentId = null, int? FilterModuleId = null,
            DateTime? FromDate = null, DateTime? ToDate = null, bool showResults = false)
        {
            var model = new QueryViewModel
            {
                FilterDepartmentId = FilterDepartmentId,
                FilterModuleId = FilterModuleId,
                FromDate = FromDate,
                ToDate = ToDate
            };

            if (showResults && model.FromDate.HasValue && model.ToDate.HasValue)
            {
                model.Results = GetQueryResults(model.FromDate, model.ToDate, model.FilterDepartmentId, model.FilterModuleId);
                ViewBag.ShowResults = true;
            }

            return View(model);
        }

        [HttpPost]
        public IActionResult QueryStatus(QueryViewModel model, string action)
        {
            // Get current user from JWT claims (now available via User property)
            string empCode = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "UNKNOWN";
            string displayName = User.FindFirst(ClaimTypes.Name)?.Value ?? empCode;

            // Handle Add Query
            if (action == "add" && model.DepartmentId.HasValue && model.ModuleId.HasValue && !string.IsNullOrWhiteSpace(model.Query))
            {
                try
                {
                    using var conn = new OracleConnection(_connStr);
                    conn.Open();

                    var nameCmd = new OracleCommand(@"
                        SELECT intrl_dept_name, module_name
                        FROM mana0809.srm_intrnl_cntrl_modules
                        WHERE intrl_dept_id = :deptId AND module_id = :modId AND module_status = 1", conn);
                    nameCmd.Parameters.Add("deptId", model.DepartmentId.Value);
                    nameCmd.Parameters.Add("modId", model.ModuleId.Value);

                    string deptName = "Unknown Department";
                    string modName = "Unknown Module";

                    using var reader = nameCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        deptName = reader.GetString(0);
                        modName = reader.GetString(1);
                    }

                    var insertCmd = new OracleCommand(@"
                        INSERT INTO mana0809.Crf_Process_mst
                        (DEPARTMENT_NAME, MODULE_NAME, CONTROL_NAME, QUERY_TEXT, ENTERED_BY)
                        VALUES (:dept, :mod, :ctrl, :qry, :emp)", conn);

                    insertCmd.Parameters.Add("dept", deptName);
                    insertCmd.Parameters.Add("mod", modName);
                    insertCmd.Parameters.Add("ctrl", string.IsNullOrWhiteSpace(model.ControlName) ? "Unnamed Activity" : model.ControlName.Trim());
                    insertCmd.Parameters.Add("qry", model.Query.Trim());
                    insertCmd.Parameters.Add("emp", empCode);

                    insertCmd.ExecuteNonQuery();

                    TempData["SuccessMessage"] = $"Query '{model.ControlName?.Trim() ?? "New Query"}' submitted successfully by {displayName}!";
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = "Failed to add query: " + ex.Message;
                    _logger.LogError(ex, "Error adding query");
                }
            }

            // Handle Filter
            if (action == "filter")
            {
                if (!model.FromDate.HasValue || !model.ToDate.HasValue)
                {
                    TempData["ErrorMessage"] = "From Date and To Date are required.";
                }
                else if (model.FromDate > model.ToDate)
                {
                    TempData["ErrorMessage"] = "From Date cannot be greater than To Date.";
                }
                else
                {
                    return RedirectToAction("QueryStatus", new
                    {
                        FilterDepartmentId = model.FilterDepartmentId,
                        FilterModuleId = model.FilterModuleId,
                        FromDate = model.FromDate,
                        ToDate = model.ToDate,
                        showResults = true
                    });
                }
            }

            return RedirectToAction("QueryStatus", new
            {
                FilterDepartmentId = model.FilterDepartmentId,
                FilterModuleId = model.FilterModuleId,
                FromDate = model.FromDate,
                ToDate = model.ToDate
            });
        }

        public IActionResult ExportToExcel(DateTime? fromDate, DateTime? toDate, int? deptId, int? modId)
        {
            var results = GetQueryResults(fromDate, toDate, deptId, modId);

            var dt = new DataTable();
            dt.Columns.Add("S NO"); dt.Columns.Add("Activity"); dt.Columns.Add("Status");
            dt.Columns.Add("Count"); dt.Columns.Add("Tech Lead Name"); dt.Columns.Add("Tester Techlead");

            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                var row = dt.NewRow();
                row["S NO"] = i + 1;
                row["Activity"] = r.ControlName ?? r.ModuleName ?? "N/A";
                row["Status"] = r.RowCount > 0 ? "Success" : "Failed";
                row["Count"] = r.RowCount;
                row["Tech Lead Name"] = r.TechLeadName ?? "";
                row["Tester Techlead"] = r.TesterTechLead ?? "";
                dt.Rows.Add(row);
            }

            using var workbook = new XLWorkbook();
            workbook.Worksheets.Add(dt, "Query Status");
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"QueryStatus_{DateTime.Now:yyyyMMdd}.xlsx");
        }

        private List<QueryItem> GetQueryResults(DateTime? fromDate = null, DateTime? toDate = null, int? deptId = null, int? modId = null)
        {
            var results = new List<QueryItem>();
            using var conn = new OracleConnection(_connStr);
            conn.Open();

            string sql = @"
                SELECT ID, DEPARTMENT_NAME, MODULE_NAME, CONTROL_NAME, QUERY_TEXT
                FROM mana0809.Crf_Process_mst
                WHERE 1 = 1";

            if (deptId.HasValue && deptId.Value > 0)
                sql += " AND DEPARTMENT_NAME IN (SELECT intrl_dept_name FROM mana0809.srm_intrnl_cntrl_modules WHERE intrl_dept_id = :deptId)";

            if (modId.HasValue && modId.Value > 0)
                sql += " AND MODULE_NAME IN (SELECT module_name FROM mana0809.srm_intrnl_cntrl_modules WHERE module_id = :modId)";

            sql += " ORDER BY ID";

            using var fetchCmd = new OracleCommand(sql, conn);
            if (deptId.HasValue && deptId.Value > 0) fetchCmd.Parameters.Add("deptId", deptId.Value);
            if (modId.HasValue && modId.Value > 0) fetchCmd.Parameters.Add("modId", modId.Value);

            using var reader = fetchCmd.ExecuteReader();
            while (reader.Read())
            {
                var item = new QueryItem
                {
                    Id = reader.GetInt32(0),
                    DepartmentName = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ModuleName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ControlName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    QueryText = reader.GetString(4)
                };

                try
                {
                    string userQuery = item.QueryText.Trim();
                    if (userQuery.EndsWith(";")) userQuery = userQuery[..^1].Trim();
                    string safeCountQuery = $"SELECT COUNT(*) FROM ({userQuery}) sub";
                    using var countCmd = new OracleCommand(safeCountQuery, conn);
                    var result = countCmd.ExecuteScalar();
                    item.RowCount = result is not null and not DBNull ? Convert.ToInt32(result) : 0;
                }
                catch (Exception ex)
                {
                    item.RowCount = 0;
                    _logger.LogWarning(ex, "Failed to execute user query for {ControlName}", item.ControlName);
                }

                results.Add(item);
            }

            return results;
        }

        public JsonResult GetDepartments()
        {
            using var conn = new OracleConnection(_connStr);
            conn.Open();

            var cmd = new OracleCommand(@"
                SELECT DISTINCT intrl_dept_id, intrl_dept_name
                FROM mana0809.srm_intrnl_cntrl_modules
                WHERE module_status = 1
                ORDER BY intrl_dept_name", conn);

            var list = new List<object>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new { id = reader.GetInt32(0), name = reader.GetString(1) });
            }
            return Json(list);
        }

        public JsonResult GetModules(int deptId)
        {
            using var conn = new OracleConnection(_connStr);
            conn.Open();

            var cmd = new OracleCommand(@"
                SELECT module_id, module_name
                FROM mana0809.srm_intrnl_cntrl_modules
                WHERE intrl_dept_id = :deptId AND module_status = 1
                ORDER BY module_name", conn);
            cmd.Parameters.Add("deptId", deptId);

            var list = new List<object>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new { id = reader.GetInt32(0), name = reader.GetString(1) });
            }
            return Json(list);
        }

        [HttpGet]
        public IActionResult KYCDeviationReport() => View(new KYCDeviationViewModel());

        [HttpPost]
        public IActionResult KYCDeviationReport(KYCDeviationViewModel model, string action)
        {
            if (!model.FromDate.HasValue || !model.ToDate.HasValue)
            {
                TempData["ErrorMessage"] = "From Date and To Date are required.";
                return View(model);
            }

            if (model.FromDate > model.ToDate)
            {
                TempData["ErrorMessage"] = "From Date cannot be greater than To Date.";
                return View(model);
            }

            model.Results = GetKYCDeviationReport(model.FromDate.Value, model.ToDate.Value);
            return View(model);
        }

        private List<KYCDeviationItem> GetKYCDeviationReport(DateTime fromDate, DateTime toDate)
        {
            var results = new List<KYCDeviationItem>();
            string fromDateStr = fromDate.ToString("dd-MMM-yyyy").ToUpper();
            string toDateStr = toDate.ToString("dd-MMM-yyyy").ToUpper();

            using var conn = new OracleConnection(_connStr);
            conn.Open();

            using var cmd = new OracleCommand("PROC_DAILY_REPORTS_MASTER", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "KYC_DEVIATION";
            cmd.Parameters.Add("p_sub_flag", OracleDbType.Varchar2).Value = DBNull.Value;
            cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = fromDateStr;
            cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = toDateStr;
            cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = DBNull.Value;
            cmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
            cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = DBNull.Value;
            cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = DBNull.Value;
            cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = DBNull.Value;
            cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new KYCDeviationItem
                {
                    SNo = reader.GetInt32(0),
                    KYCDeviation = reader.GetString(1),
                    Count = reader.GetInt32(2),
                    Status = reader.IsDBNull(3) ? "" : reader.GetString(3)
                });
            }

            return results;
        }

        [HttpGet]
        public IActionResult DailyCustomerData()
        {
            var model = new DailyCustomerDataViewModel();

            try
            {
                using var conn = new OracleConnection(_connStr);
                conn.Open();

                using var cmd = new OracleCommand("PROC_DAILY_REPORTS_MASTER", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.Add("p_flag", OracleDbType.Varchar2).Value = "DAILY_CUSTOMER_DATA";
                cmd.Parameters.Add("p_sub_flag", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_from_date", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_to_date", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_release_type", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_tl", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_tester_name", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_json_input", OracleDbType.Clob).Value = DBNull.Value;
                cmd.Parameters.Add("p_updated_by", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_emp_code", OracleDbType.Int32).Value = DBNull.Value;
                cmd.Parameters.Add("p_pageval", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_parval1", OracleDbType.Varchar2).Value = DBNull.Value;
                cmd.Parameters.Add("p_result", OracleDbType.RefCursor).Direction = ParameterDirection.Output;

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    model.TotalCustomers = reader.GetInt32("total_customers");
                    model.HypervergeCustomers = reader.GetInt32("hyperverge_customers");
                    model.JukshioCustomers = reader.GetInt32("jukshio_customers");
                    model.DoorstepCustomers = reader.GetInt32("doorstep_customers");
                    model.AadharCount = reader.GetInt32("aadhar_count");
                    model.DigiCount = reader.GetInt32("digi_count");
                    model.OtherIdCount = reader.GetInt32("other_id_count");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Daily Customer Data");
                TempData["ErrorMessage"] = "Unable to load customer data.";
            }

            return View(model);
        }

        public JsonResult GetTestersForTL(string tlName)
        {
            if (string.IsNullOrWhiteSpace(tlName)) return Json(new List<object>());

            int? subTeam = tlName.Trim().ToUpper() switch
            {
                "JIJIN E H" => 1,
                "MURUGESAN P" => 2,
                "NIKHIL SEKHAR" => 3,
                "SMINA BENNY" => 4,
                "VISAGH S" => 5,
                "JOBY JOSE" => 6,
                _ => null
            };

            if (!subTeam.HasValue) return Json(new List<object>());

            var testers = new List<object>();

            using var conn = new OracleConnection(_connStr);
            conn.Open();

            var cmd = new OracleCommand(@"
                SELECT DISTINCT v.emp_name AS tester_name, v.emp_code AS tester_code
                FROM mana0809.srm_it_team_members t
                JOIN mana0809.employee_master v ON t.member_id = v.emp_code
                WHERE t.team_id = 6
                  AND t.sub_team = :subTeam
                  AND v.status_id = 1
                ORDER BY v.emp_name", conn);

            cmd.Parameters.Add("subTeam", OracleDbType.Int32).Value = subTeam.Value;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                testers.Add(new
                {
                    text = reader["tester_name"]?.ToString()?.Trim() ?? "Unknown",
                    value = reader["tester_code"]?.ToString()?.Trim() ?? ""
                });
            }

            return Json(testers);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        private List<SelectListItem> GetTesterTLs()
        {
            var list = new List<SelectListItem>
            {
                new SelectListItem { Text = "All TLs", Value = "" }
            };

            using var conn = new OracleConnection(_connStr);
            conn.Open();

            var cmd = new OracleCommand(@"
                SELECT DISTINCT
                    CASE
                        WHEN t.sub_team = 1 THEN 'JIJIN E H'
                        WHEN t.sub_team = 2 THEN 'MURUGESAN P'
                        WHEN t.sub_team = 3 THEN 'NIKHIL SEKHAR'
                        WHEN t.sub_team = 4 THEN 'SMINA BENNY'
                        WHEN t.sub_team = 5 THEN 'VISAGH S'
                        WHEN t.sub_team = 6 THEN 'JOBY JOSE'
                        ELSE NULL
                    END AS tester_tl_name
                FROM mana0809.srm_it_team_members t
                JOIN mana0809.employee_master v ON t.member_id = v.emp_code
                WHERE v.status_id = 1
                  AND t.team_id = 6
                  AND t.sub_team IS NOT NULL
                  AND t.sub_team IN (1,2,3,4,5,6)
                ORDER BY tester_tl_name", conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var tlName = reader["tester_tl_name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(tlName) && !list.Any(x => x.Value == tlName))
                {
                    list.Add(new SelectListItem { Text = tlName, Value = tlName });
                }
            }

            return list.OrderBy(x => x.Text).ToList();
        }
    }
}