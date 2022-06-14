using System;
using System.Net.Http;
using System.Net;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
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

            string groupId = req.Query["groupId"];
            string reportId = req.Query["reportId"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic requestData = JsonConvert.DeserializeObject(requestBody);
            groupId = groupId ?? requestData?.groupId;
            reportId = reportId ?? requestData?.reportId;

            HttpClient httpClient = new HttpClient();
            var powerBI_API_URL = "api.powerbi.com";
            var powerBI_API_Scope = "https://analysis.windows.net/powerbi/api/.default";

            // Azure App Registration
            var clientId = "430162f7-824d-4ae0-8f94-7283bbf4ca7a";
            // string clientId = Environment.GetEnvironmentVariables("ClientId");
            log.LogInformation(clientId);
            var clientSecret = "ldu8Q~lc4CISYqtoQ-1~nG7ycP_Cck6m0-uL2bJI";
            // string clientSecret = Environment.GetEnvironmentVariables("ClientSecret");
            log.LogInformation(clientSecret);
            var tenantId = "4ec55493-6b1c-4565-a868-2ae940882c82";
            // string tenantId = Environment.GetEnvironmentVariables("TenantId");
            log.LogInformation(tenantId);
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
            // Get PowerBi report url and embed token
            // HttpClient powerBiClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
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
            var tokenContent = new FormUrlEncodedContent(new[]
            {
        new KeyValuePair<string, string>("accessLevel", "View")
    });
            var embedToken = await httpClient.PostAsync($"https://{powerBI_API_URL}/v1.0/myorg/groups/{groupId}/reports/{reportId}/GenerateToken", tokenContent)
                .ContinueWith<string>((response) =>
                {
                    log.LogInformation(response.Result.StatusCode.ToString());
                    log.LogInformation(response.Result.ReasonPhrase.ToString());
                    PowerBiEmbedToken powerBiEmbedToken =
                        JsonConvert.DeserializeObject<PowerBiEmbedToken>(response.Result.Content.ReadAsStringAsync().Result);
                    return powerBiEmbedToken?.Token;
                });
            // JSON Response
            EmbedContent data = new EmbedContent
            {
                EmbedToken = embedToken,
                EmbedUrl = embedUrl,
                ReportId = reportId,
                AccessToken = accessToken
            };
            string jsonp = JsonConvert.SerializeObject(data);
            // Return Response
            
            // return new HttpResponseMessage(HttpStatusCode.OK)
            // {
            //     Content = new StringContent(jsonp, Encoding.UTF8, "application/json")
            // };

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
    }
}
