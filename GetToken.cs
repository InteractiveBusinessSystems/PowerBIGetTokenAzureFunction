using System;
using System.Net.Http;
using System.Net;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Company.Function
{
    public static class GetToken
    {
        [FunctionName("GetToken")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {

            StreamReader sr = new StreamReader(req.Body);
            string json = sr.ReadToEnd();
            var reports = JsonConvert.DeserializeObject<List<Report>>(json);

            HttpClient httpClient = new HttpClient();

            var powerBI_API_URL = "api.powerbi.com";
            var powerBI_API_Scope = "https://analysis.windows.net/powerbi/api/.default";

            // Lines 35-63 are for using a Service Principal Account to authenticate to Azure AD - 
            //in production this requires the purchase of a Capacity.
            // Variables for Azure App Registration
            string clientId = Environment.GetEnvironmentVariable("ClientId");
            string clientSecret = Environment.GetEnvironmentVariable("ClientSecret");
            string tenantId = Environment.GetEnvironmentVariable("TenantId");

            var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("scope", powerBI_API_Scope),
                    new KeyValuePair<string, string>("client_secret", clientSecret)
                    });


            // Generate Access Token to authenticate for Power BI
            var accessToken = await httpClient.PostAsync($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token", content).ContinueWith<string>((response) =>
           {
               log.LogInformation(response.Result.StatusCode.ToString());
               log.LogInformation(response.Result.ReasonPhrase.ToString());
               log.LogInformation(response.Result.Content.ReadAsStringAsync().Result);
               AzureAdTokenResponse tokenRes =
                   JsonConvert.DeserializeObject<AzureAdTokenResponse>(response.Result.Content.ReadAsStringAsync().Result);
               return tokenRes?.AccessToken; ;
           });

        //     //Lines 66-96 are for using a Master User account, but I haven't figured out how to make that work yet
        //     // Use a Master User to Authenticate to AAD
        //     // Azure App Registration
        //     // string tenantId = Environment.GetEnvironmentVariable("TenantId");
        //     // string clientId = Environment.GetEnvironmentVariable("ClientId");
        //     // string userName = Environment.GetEnvironmentVariable("UserName");
        //     // string password = Environmnet.GetEnvironmentVariable("Password");
        //     SecureString securePassword = new SecureString();
        //     foreach (var key in password){
        //         securePassword.AppendChar(key);
        //     }

        //     var content = new FormUrlEncodedContent(new[]
        //         {
        //             new KeyValuePair<string, string>("grant_type", "password"),
        //             new KeyValuePair<string, string>("client_id", clientId),
        //             new KeyValuePair<string, string>("scopes", powerBI_API_Scope),
        //             new KeyValuePair<string, string>("username", userName),
        //             new KeyValuePair<string, string>("password", password)
        //             });

        //     // Generate Access Token to authenticate for Power BI
        //     var accessToken = await httpClient.PostAsync($"https://login.microsoftonline.com/{tenantId}", content).ContinueWith<string>((response) =>
        //    {
        //        log.LogInformation(response.Result.StatusCode.ToString());
        //        log.LogInformation(response.Result.ReasonPhrase.ToString());
        //        log.LogInformation(response.Result.Content.ReadAsStringAsync().Result);
        //        AzureAdTokenResponse tokenRes =
        //            JsonConvert.DeserializeObject<AzureAdTokenResponse>(response.Result.Content.ReadAsStringAsync().Result);
        //        return tokenRes?.AccessToken; ;
        //    });

           //Add accessToken to header for Http calls
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            //Build the array to pass to the Http call to retrieve the embed token
            var datasets = new List<DataSetModel>();
            var reportIds = new List<ReportModel>();

            foreach(var report in reports){
                datasets.Add(new DataSetModel{id=report.dataSetsId});
                reportIds.Add(new ReportModel{id=report.reportId});
            };

            var data = new {
                datasets = datasets,
                reports = reportIds
            };
   
            var dataString = JsonConvert.SerializeObject(data);
            var stringContent = new StringContent(dataString, Encoding.UTF8, "application/json");

            //Get embedToken for the reports
            var embedToken = await httpClient.PostAsync($"https://{powerBI_API_URL}/v1.0/myorg/GenerateToken", stringContent)
                .ContinueWith<string>((response) =>
                {
                    log.LogInformation(response.Result.StatusCode.ToString());
                    log.LogInformation(response.Result.ReasonPhrase.ToString());
                    PowerBiEmbedToken powerBiEmbedToken =
                        JsonConvert.DeserializeObject<PowerBiEmbedToken>(response.Result.Content.ReadAsStringAsync().Result);
                    return powerBiEmbedToken?.Token;
                });

            //Get PowerBI report url for each report
            foreach(var report in reports){

                var groupId = report.workspaceId;
                var reportId = report.reportId;
                 
                // Get PowerBi report url
                var embedUrl =
                    await httpClient.GetAsync($"https://{powerBI_API_URL}/v1.0/myorg/groups/{groupId}/reports/{reportId}")
                    .ContinueWith<string>((response) =>
                    {
                        log.LogInformation(response.Result.StatusCode.ToString());
                        log.LogInformation(response.Result.ReasonPhrase.ToString());
                        PowerBiReport report =
                            JsonConvert.DeserializeObject<PowerBiReport>(response.Result.Content.ReadAsStringAsync().Result);
                        return report?.EmbedUrl;
                    });

                // add embedUrl, accessToken and embedToken to report data
                report.embedUrl = embedUrl;
                report.accessToken = accessToken;
                report.embedToken = embedToken;
            };

            //Serialize and return the new reports array
            string jsonp = JsonConvert.SerializeObject(reports);
            return new OkObjectResult(jsonp);
        }
        public class AzureAdTokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }
        }
        public class PowerBiReport
        {
            [JsonProperty(PropertyName = "id")]
            public string Id { get; set; }
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }
            [JsonProperty(PropertyName = "webUrl")]
            public string WebUrl { get; set; }
            [JsonProperty(PropertyName = "embedUrl")]
            public string EmbedUrl { get; set; }
            [JsonProperty(PropertyName = "datasetId")]
            public string DatasetId { get; set; }
        }
        public class PowerBiEmbedToken
        {
            [JsonProperty(PropertyName = "token")]
            public string Token { get; set; }
            [JsonProperty(PropertyName = "tokenId")]
            public string TokenId { get; set; }
            [JsonProperty(PropertyName = "expiration")]
            public DateTime? Expiration { get; set; }
        }
        public class EmbedContent
        {
            public string EmbedToken { get; set; }
            public string EmbedUrl { get; set; }
            public string ReportId { get; set; }
            public string AccessToken { get; set; }
        }

        public class UsersWhoCanView
        {
            [JsonProperty(PropertyName = "odata.type")]
            public string odataType { get; set; }
            [JsonProperty(PropertyName = "odata.id")]
            public string odataId { get; set; }
            [JsonProperty(PropertyName = "Name")]
            public string name { get; set; }
        }

        public class Report
        {
            [JsonProperty(PropertyName = "ReportName")]
            public string reportName { get; set; }
            [JsonProperty(PropertyName = "DataSetsId")]
            public string dataSetsId { get; set; }
            [JsonProperty(PropertyName = "WorkspaceId")]
            public string workspaceId { get; set; }
            [JsonProperty(PropertyName = "ReportId")]
            public string reportId { get; set; }
            [JsonProperty(PropertyName = "ReportSectionId")]
            public string reportSectionId { get; set; }
            [JsonProperty(PropertyName = "ReportUrl")]
            public string reportUrl { get; set; }
            [JsonProperty(PropertyName = "ViewerType")]
            public string viewerType { get; set; }
            [JsonProperty(PropertyName = "UsersWhoCanView")]
            public List<UsersWhoCanView> usersWhoCanView { get; set; }
             [JsonProperty(PropertyName = "Id")]
            public string Id { get; set; }
            [JsonProperty(PropertyName = "EmbedToken")]
            public string embedToken { get; set; }
            [JsonProperty(PropertyName = "EmbedUrl")]
            public string embedUrl { get; set; }
            [JsonProperty(PropertyName = "AccessToken")]
            public string accessToken { get; set; }
            
        }

        public class PowerBICredsModel {
            public string grant_type { get; set; }
            public string client_id { get; set; }
            public string scopes { get; set; }
            public string username { get; set; }
            public SecureString password { get; set; }
        }

        public class DataSetModel {
            public string id { get; set; }
        }

        public class ReportModel {
            public string id { get; set; }
        }

    }
}
