﻿using System.Data;
using System.Data.SqlTypes;

namespace API
{
    /// <summary>
    /// CacheTrace
    /// </summary>
    public class DatabaseTrace
    {

        internal static DataTable CreateDatabaseTraceDataTable()
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("DBT_CORRELATION_ID");
            dataTable.Columns.Add("DBT_PROCEDURE_NAME");
            dataTable.Columns.Add("DBT_PARAMS");
            dataTable.Columns.Add("DBT_START_TIME");
            dataTable.Columns.Add("DBT_DURATION", typeof(SqlDecimal));
            dataTable.Columns.Add("DBT_ACTION");
            dataTable.Columns.Add("DBT_SUCCESS");
            return dataTable;
        }


        //adds rows to databaseTraceDataTable asynclocal variable
        internal static  void PopulateDatabaseTrace(string procedure, string procedureParams, DateTime startTime, decimal duration, string action, bool success)
        {
            //dont record tracing unless apiconfiguration has been loaded and their is a correlation id
            if (ApiServicesHelper.ApiConfiguration != null && APIMiddleware.correlationID.Value != null && APIMiddleware.databaseTraceDataTable.Value != null)
            {
                try
                {
                    if (ApiServicesHelper.DatabaseTracingConfiguration.API_DATABASE_TRACE_ENABLED)
                    {
                        Common common = new Common();
                        procedureParams = common.MaskParameters(procedureParams);
                        APIMiddleware.databaseTraceDataTable.Value.Rows.Add(APIMiddleware.correlationID.Value, procedure,procedureParams, startTime, duration, action, success);
                    }
                }catch(Exception ex)
                {
                    Log.Instance.Error(ex);
                }
             
            }
        }
    }
}
