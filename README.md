# PowerBIGetTokenAzureFunction
Azure Function to retrieve embed token and embed urls for Power BI reports

Uses an Azure AD app registration to authenticate to Microsoft. The values for the Tenant Id, Client Id and Client Secret are stored in environment variables.
Uses the access token received from Microsoft to retrieve the embed token and embed urls from Power BI for the report(s) that were passed to the function in parameters.
Adds the values of the access token, embed token and embed url to the original reports array and returns the new array.

Built using .Net 3.1

