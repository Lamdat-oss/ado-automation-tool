using System.Text;
using System.Security.Cryptography;
using System.Xml;
using Lamdat.ADOAutomationTool.Entities;

namespace Lamdat.ADOAutomationTool.Service
{
    /// <summary>
    /// Fetch files from S3 compatible storages like Backplaze
    /// </summary>
    public class S3StorageClient : IS3StorageClient
    {
        private readonly string _bucketName;
        private readonly string _accessKey;
        private readonly string _secretKey;
        private readonly string _serviceUrl;
        private readonly string _region;
        private readonly string? _s3FolderPath;
        private readonly RulesStorageType _rulesStorageType;

        private const string FOLDER_PATH = "Scripts";
        private const string SERVICE = "s3";
        private const string ALGORITHM = "AWS4-HMAC-SHA256";

        private readonly Serilog.ILogger _logger;

        /// <summary>
        /// Fetch files from S3 compatible storages like Backplaze
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="rulesStorageType">Can be Amazon, Backblaze</param>
        /// <param name="region">S3 region</param>
        /// <param name="serviceUrl">B2 S3 compatible endpoint</param>
        /// <param name="bucketName">B2 bucket name</param>
        /// <param name="secretKey">B2 secret key</param>
        /// <param name="accessKey">B2 access key</param>
        /// <param name="s3FolderPath">S3 folder prefix (optional)</param>
        /// <exception cref="ArgumentNullException"></exception>
        public S3StorageClient(Serilog.ILogger logger, RulesStorageType rulesStorageType, string region, string serviceUrl, string bucketName, string secretKey, string accessKey, string? s3FolderPath)
        {
            if (string.IsNullOrEmpty(serviceUrl))
                throw new ArgumentNullException(nameof(serviceUrl));
            
            if (string.IsNullOrEmpty(region))
                throw new ArgumentNullException(nameof(region));
            
            if (string.IsNullOrEmpty(bucketName))
                throw new ArgumentNullException(nameof(bucketName));
            
            if (string.IsNullOrEmpty(secretKey))
                throw new ArgumentNullException(nameof(secretKey));
            
            if (string.IsNullOrEmpty(accessKey))
                throw new ArgumentNullException(nameof(accessKey));

            _bucketName = bucketName;
            _accessKey = accessKey;
            _secretKey = secretKey;
            _serviceUrl = serviceUrl;
            _region = region;
            _s3FolderPath = s3FolderPath;
            _rulesStorageType = rulesStorageType;
            
            _logger = logger;
        }
        
        

        public async Task DownloadRules()
        {
            _logger.Information($"Downloading rules from '{_serviceUrl}'");
            
            // 1. Get existing local rules to avoid the saving of a file with same name
            
            var existingRules = GetExistingLocalScriptsList();
            
            
            // 2. Get list of files from the bucket
            
            var url = $"{_serviceUrl}";
            if (_rulesStorageType == RulesStorageType.Backblaze)
            {
                url += $"/{_bucketName}";
            }
            
            var client = GetConnectionForGetRequest(url);
            
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var xmlString = await response.Content.ReadAsStringAsync();
                
                List<string> fileNamesWithPaths = GetFileNamesWithPaths(xmlString);
           
                _logger.Information($"     Found {fileNamesWithPaths.Count} *.rule files in the bucket '{_bucketName}'");
                
                // 3. If exist any *.rule in the bucket then download them one by one into local folder
                
                if (fileNamesWithPaths.Count > 0)
                {
                    foreach (var fileNameWithPath in fileNamesWithPaths)
                    {
                        string fileName = Path.GetFileName(fileNameWithPath);
                
                        // if ( ! existingRules.Contains(fileName))
                        // {
                            var fileUrl = url + '/' + fileNameWithPath;
                            
                            var clientForGetFileContent = GetConnectionForGetRequest(fileUrl);
                            var responseOneRule = await clientForGetFileContent.GetAsync(fileUrl);
                
                            if (responseOneRule.IsSuccessStatusCode)
                            {
                            
                                string localFilePath = Path.Combine(FOLDER_PATH, fileName);
                                
                                await using var responseStream = await responseOneRule.Content.ReadAsStreamAsync();
                                await using var fileStream = File.Create(localFilePath);
                                await responseStream.CopyToAsync(fileStream);
                                
                                _logger.Information($"     A file '{fileNameWithPath}' has been downloaded successfully");
                            }
                            else
                            {
                                var apiErrorMsg = await responseOneRule.Content.ReadAsStringAsync();
                                var errorMessage = $"Failed to fetch a file '{fileName}' from the S3 storage: {(int)responseOneRule.StatusCode} {responseOneRule.ReasonPhrase} {apiErrorMsg}";
                                _logger.Error(errorMessage);
                            }   
                        // }
                    }
                }
            }
            else
            {
                var apiErrorMsg = await response.Content.ReadAsStringAsync();
                var errorMessage = $"Failed to execute AWS request: {(int)response.StatusCode} {response.ReasonPhrase} {apiErrorMsg}";
                _logger.Error(errorMessage);
            }
            
        }

        private String[] GetExistingLocalScriptsList()
        {
            string scriptsDirectory = "scripts";
            if (!Directory.Exists(scriptsDirectory))
            {
                throw new Exception("Scripts Directory not found");
            }
            string[] existingScriptFiles = Directory.GetFiles(scriptsDirectory, "*.rule");
            var existingRules = existingScriptFiles.Select(r => Path.GetFileName(r)).ToArray();

            return existingRules;
        }


        private List<string> GetFileNamesWithPaths(string xmlString)
        {
            List<string> fileNamesWithPaths = new List<string>();
                
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xmlString);
            
            for (int i = 0; i < doc.ChildNodes.Count; i++)
            {
                var listBucketResultNode = doc.ChildNodes[i];
                if (listBucketResultNode != null && listBucketResultNode.Name == "ListBucketResult")
                {
                    for (int ii = 0; ii < listBucketResultNode.ChildNodes.Count; ii++)
                    {
                        var contentsNode = listBucketResultNode.ChildNodes[ii];
                        if (contentsNode != null && contentsNode.Name == "Contents")
                        {
                            for (int iii = 0; iii < contentsNode.ChildNodes.Count; iii++)
                            {
                                var keyNode = contentsNode.ChildNodes[iii];
                                if (keyNode != null && keyNode.Name == "Key")
                                {
                                    var fileName = keyNode.InnerText;
                                    if (!string.IsNullOrWhiteSpace(fileName))
                                    {
                                        if (fileName[0] == '/' || fileName[0] == '\\')
                                        {
                                            fileName = fileName.Substring(1, fileName.Length - 1);
                                        }
                                    
                                        var lastIndex = fileName.Length - 1;
                                        if (lastIndex > 0 && (fileName[lastIndex] == '/' || fileName[lastIndex] == '\\'))
                                        {
                                            fileName = fileName.Substring(0, fileName.Length - 1);
                                        }
                                        
                                        if (!string.IsNullOrWhiteSpace(fileName) &&
                                            fileName.EndsWith(".rule"))
                                        {
                                            if (string.IsNullOrWhiteSpace(_s3FolderPath) ||
                                                (!string.IsNullOrWhiteSpace(_s3FolderPath) && fileName.StartsWith(_s3FolderPath)))
                                            {
                                                fileNamesWithPaths.Add(fileName);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return fileNamesWithPaths;
        }
            
        
        private HttpClient GetConnectionForGetRequest(string url)
        {
            var uri = new Uri(url);        
            var now = DateTime.UtcNow;
            var amzDate = ToAmzDate(now);
            
            var payloadHash = CalculateHash("");
            var authorizationHeader = GetAuthorizationHeader(SERVICE, _region,"GET", uri, now, new Dictionary<string, string>(), payloadHash);

            // Make the request
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Host", uri.Host);
            client.DefaultRequestHeaders.Add("x-amz-date", amzDate);
            client.DefaultRequestHeaders.Add("x-amz-content-sha256", payloadHash);
            
            /*
             * From docs:
             * https://github.com/aws-samples/sigv4-signing-examples/blob/main/README.md
             *
             *
             * The examples in this repository use temporary credentials.
             * These are short-lived access credentials and are preferred to long-lived security credentials where possible.
             * For example these might be provided by assuming a role or vended by a token management service.
             * If you want to change any of the examples to use long-lived security credentials instead,
             * simply remove the x-amz-security-token header from the request.
             */
            // if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN")))
            // {
            //     client.DefaultRequestHeaders.Add("x-amz-security-token", Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN"));
            // }   
            
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);

            return client;
        }
        
        
        private string ToAmzDate(DateTime date)
        {
            return date.ToString("yyyyMMddTHHmmssZ");
        }
        
        private string GetAuthorizationHeader(
            string service, string region, string httpMethod,
            Uri uri, DateTime now, IDictionary<string, string> headers, string payloadHash)
        {
            var amzDate = ToAmzDate(now);
            var datestamp = now.ToString("yyyyMMdd");
            headers.Add("host", uri.Host);
            headers.Add("x-amz-date", amzDate);
            headers.Add("x-amz-content-sha256", payloadHash);
            
            
            /*
             * From docs:
             * https://github.com/aws-samples/sigv4-signing-examples/blob/main/README.md
             *
             * 
             * The examples in this repository use temporary credentials.
             * These are short-lived access credentials and are preferred to long-lived security credentials where possible.
             * For example these might be provided by assuming a role or vended by a token management service.
             * If you want to change any of the examples to use long-lived security credentials instead,
             * simply remove the x-amz-security-token header from the request.
             */
            // if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN")))
            // {
            //     headers.Add("x-amz-security-token", Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN"));
            // }

            
            // Create the canonical request        
            var canonicalQuerystring = "";
            var canonicalHeaders = CanonicalizeHeaders(headers);
            var signedHeaders = CanonicalizeHeaderNames(headers);
            var canonicalRequest = $"{httpMethod}\n{uri.AbsolutePath}\n{canonicalQuerystring}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

            // Create the string to sign
            var credentialScope = $"{datestamp}/{region}/{service}/aws4_request";
            var hashedCanonicalRequest = CalculateHash(canonicalRequest);
            var stringToSign = $"{ALGORITHM}\n{amzDate}\n{credentialScope}\n{hashedCanonicalRequest}";
       
            // Sign the string
            var signingKey = GetSignatureKey(_secretKey, datestamp, region, service);
            var signature = CalculateHmacHex(signingKey, stringToSign);
        
            // return signing information
            return $"{ALGORITHM} Credential={_accessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
        }
        
        private string CanonicalizeHeaderNames(IDictionary<string, string> headers)
        {
            var headersToSign = new List<string>(headers.Keys);
            headersToSign.Sort(StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            foreach (var header in headersToSign)
            {
                if (sb.Length > 0)
                    sb.Append(";");
                sb.Append(header.ToLower());
            }
            return sb.ToString();
        }

        private string CanonicalizeHeaders(IDictionary<string, string> headers)
        {
            var canonicalHeaders = new StringBuilder();
            var sortedHeaders = new SortedDictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            foreach (var header in sortedHeaders)
            {
                canonicalHeaders.Append($"{header.Key.ToLowerInvariant()}:{header.Value.Trim()}\n");
            }
            return canonicalHeaders.ToString();
        }
        
        private string CalculateHash(string data)
        {
            using SHA256 sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
        {
            var kSecret = Encoding.UTF8.GetBytes($"AWS4{key}");
            var kDate = HmacSha256(kSecret, dateStamp);
            var kRegion = HmacSha256(kDate, regionName);
            var kService = HmacSha256(kRegion, serviceName);
            return HmacSha256(kService, "aws4_request");
        }

        private byte[] HmacSha256(byte[] key, string data)
        {
            using HMACSHA256 hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }
        
        private string CalculateHmacHex(byte[] key, string data)
        {
            var hash = HmacSha256(key, data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }   
}