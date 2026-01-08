using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;
using QA.API.Models;
using QA.Application.Models;
using System;
using System.Collections.Generic;

namespace QA.Application.Controllers
{
    public class ProcedureMonitorController : Controller
    {
        private readonly string _oracleConnectionString;
        private const int PageSize = 10;

        public ProcedureMonitorController(IConfiguration configuration)
        {
            _oracleConnectionString = configuration.GetConnectionString("OracleConnection");
        }

        public IActionResult Index(DateTime? fromDate = null, DateTime? toDate = null, string search = "", int page = 1)
        {
            List<ProcedureStatusViewModel> allProcedures = new List<ProcedureStatusViewModel>();

            if (fromDate.HasValue && toDate.HasValue)
            {
                // Get ALL executions (no limit)
                allProcedures = GetAllExecutionsInRange(fromDate.Value, toDate.Value);

                // Apply search server-side if needed (optional, but helps performance)
                if (!string.IsNullOrEmpty(search))
                {
                    string lowerSearch = search.ToLower();
                    allProcedures = allProcedures.Where(p => p.ModuleName.ToLower().Contains(lowerSearch)).ToList();
                }

                ViewBag.FromDate = fromDate.Value.ToString("yyyy-MM-dd");
                ViewBag.ToDate = toDate.Value.ToString("yyyy-MM-dd");
                ViewBag.SearchTerm = search;
                ViewBag.ShowResults = true;
                ViewBag.TotalRecords = allProcedures.Count;
            }
            else
            {
                ViewBag.FromDate = DateTime.Today.ToString("yyyy-MM-dd");
                ViewBag.ToDate = DateTime.Today.ToString("yyyy-MM-dd");
                ViewBag.SearchTerm = "";
                ViewBag.ShowResults = false;
            }

            ViewBag.Today = DateTime.Today.ToString("dddd, dd MMMM yyyy");

            return View(allProcedures); // Send ALL data to view
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(string fromDate, string toDate, string searchTerm = "")
        {
            DateTime parsedFrom = DateTime.Parse(fromDate);
            DateTime parsedTo = DateTime.Parse(toDate);

            return RedirectToAction("Index", new { fromDate = parsedFrom, toDate = parsedTo, search = searchTerm });
        }

        private List<ProcedureStatusViewModel> GetAllExecutionsInRange(DateTime fromDate, DateTime toDate, string searchTerm = "")
        {
            var result = new List<ProcedureStatusViewModel>();

            string query = @"
                SELECT MODULE, IN_TIME, OUT_TIME
                FROM mana0809.updation_log
                WHERE TRUNC(IN_TIME) BETWEEN :FromDate AND :ToDate
                " + (!string.IsNullOrEmpty(searchTerm) ? "AND UPPER(MODULE) LIKE '%' || UPPER(:SearchTerm) || '%'" : "") + @"
                ORDER BY IN_TIME DESC";

            using (var connection = new OracleConnection(_oracleConnectionString))
            using (var cmd = new OracleCommand(query, connection))
            {
                cmd.Parameters.Add("FromDate", OracleDbType.Date).Value = fromDate.Date;
                cmd.Parameters.Add("ToDate", OracleDbType.Date).Value = toDate.Date;

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    cmd.Parameters.Add("SearchTerm", OracleDbType.Varchar2).Value = searchTerm;
                }

                connection.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string module = reader["MODULE"]?.ToString()?.Trim() ?? "Unknown";
                        var inTimeDb = reader["IN_TIME"] as DateTime?;
                        var outTimeDb = reader["OUT_TIME"] as DateTime?;

                        string inStr = inTimeDb.HasValue ? inTimeDb.Value.ToString("hh:mm:ss tt") : "-";
                        string outStr = outTimeDb.HasValue ? outTimeDb.Value.ToString("hh:mm:ss tt") : null;

                        bool isWorking = inTimeDb.HasValue && outTimeDb.HasValue;
                        string statusText = isWorking ? "Working" : "Not Working";

                        result.Add(new ProcedureStatusViewModel
                        {
                            ModuleName = module,
                            InTime = inStr,
                            OutTime = outStr,
                            IsRunningToday = isWorking,
                            StatusText = statusText
                        });
                    }
                }
            }

            return result;
        }
    }
}