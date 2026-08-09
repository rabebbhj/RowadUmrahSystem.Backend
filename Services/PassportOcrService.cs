using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;

namespace RowadUmrahSystem.Web.Services
{
    public class PassportOcrResult
    {
        public string PassportNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public DateTime? PassportExpiryDate { get; set; }
    }

    public class PassportImageValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class PassportOcrService
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public PassportOcrService(
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public PassportImageValidationResult ValidateImage(IFormFile image)
        {
            if (image == null)
            {
                return new PassportImageValidationResult
                {
                    IsValid = false,
                    Message = "لم يتم اختيار صورة."
                };
            }

            string extension = Path.GetExtension(image.FileName).ToLower();

            string[] allowed =
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

            if (!allowed.Contains(extension))
            {
                return new PassportImageValidationResult
                {
                    IsValid = false,
                    Message = "نوع الملف غير مدعوم. الرجاء رفع صورة بصيغة JPG أو PNG أو WEBP."
                };
            }

            return new PassportImageValidationResult
            {
                IsValid = true
            };
        }

        public async Task<PassportOcrResult> ReadPassportAsync(string imagePath)
        {
            string? endpoint = _configuration["AzureDocumentIntelligence:Endpoint"];
            string? key = _configuration["AzureDocumentIntelligence:Key"];

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Azure Document Intelligence settings are missing.");
            }

            var client = new DocumentIntelligenceClient(
                new Uri(endpoint),
                new AzureKeyCredential(key));

            string physicalPath = Path.Combine(
                _environment.WebRootPath,
                imagePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

            if (!File.Exists(physicalPath))
            {
                throw new FileNotFoundException("Passport image was not found.", physicalPath);
            }

            byte[] fileBytes = await File.ReadAllBytesAsync(physicalPath);
            BinaryData documentData = BinaryData.FromBytes(fileBytes);

            Operation<AnalyzeResult> operation =
                await client.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    "prebuilt-idDocument",
                    documentData);

            AnalyzeResult result = operation.Value;

            if (result.Documents == null || result.Documents.Count == 0)
            {
                return new PassportOcrResult();
            }

            AnalyzedDocument document = result.Documents[0];

            var output = new PassportOcrResult
            {
                PassportNumber = GetFieldContent(document, "DocumentNumber"),
                FullName = BuildFullName(document),
                Nationality = GetFieldContent(document, "Nationality"),
                Gender = GetFieldContent(document, "Sex"),
                DateOfBirth = GetDateField(document, "DateOfBirth"),
                PassportExpiryDate = GetDateField(document, "DateOfExpiration")
            };

            string allText = result.Content ?? string.Empty;
            var mrz = TryParseMrz(allText);

            if (mrz != null)
            {
                if (ShouldUseMrzPassportNumber(output.PassportNumber, mrz.PassportNumber))
                    output.PassportNumber = mrz.PassportNumber;

                if (!string.IsNullOrWhiteSpace(mrz.FullName))
                    output.FullName = mrz.FullName;

                if (!string.IsNullOrWhiteSpace(mrz.Nationality))
                    output.Nationality = mrz.Nationality;

                if (!string.IsNullOrWhiteSpace(mrz.Gender))
                    output.Gender = mrz.Gender;

                if (mrz.DateOfBirth.HasValue)
                    output.DateOfBirth = mrz.DateOfBirth;

                if (mrz.PassportExpiryDate.HasValue)
                    output.PassportExpiryDate = mrz.PassportExpiryDate;
            }

            output.PassportNumber = CleanPassportNumber(output.PassportNumber);
            output.FullName = CleanName(output.FullName);
            output.Nationality = NormalizeNationality(output.Nationality);
            output.Gender = NormalizeGender(output.Gender);

            if (output.DateOfBirth.HasValue &&
                output.DateOfBirth.Value.Year < 1900)
            {
                output.DateOfBirth = null;
            }

            if (output.PassportExpiryDate.HasValue &&
                output.PassportExpiryDate.Value.Year < DateTime.UtcNow.Year - 20)
            {
                output.PassportExpiryDate = null;
            }

            return output;
        }

        private static string GetFieldContent(AnalyzedDocument document, string fieldName)
        {
            if (document.Fields.TryGetValue(fieldName, out DocumentField? field))
            {
                return field.Content ?? string.Empty;
            }

            return string.Empty;
        }

        private static DateTime? GetDateField(AnalyzedDocument document, string fieldName)
        {
            if (document.Fields.TryGetValue(fieldName, out DocumentField? field))
            {
                string value = field.Content ?? string.Empty;

                if (DateTime.TryParse(value, out DateTime parsedDate))
                {
                    return parsedDate;
                }
            }

            return null;
        }

        private static string BuildFullName(AnalyzedDocument document)
        {
            string firstName = GetFieldContent(document, "FirstName");
            string lastName = GetFieldContent(document, "LastName");

            string fullName = $"{firstName} {lastName}".Trim();

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                return fullName;
            }

            return GetFieldContent(document, "FullName");
        }

        private class MrzResult
        {
            public string PassportNumber { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Nationality { get; set; } = string.Empty;
            public string Gender { get; set; } = string.Empty;
            public DateTime? DateOfBirth { get; set; }
            public DateTime? PassportExpiryDate { get; set; }
        }

        private static MrzResult? TryParseMrz(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var lines = text
                .Split('\n', '\r')
                .Select(l => NormalizeMrzLine(l))
                .Where(l => l.Length >= 25)
                .ToList();

            string? line1 = lines.FirstOrDefault(l =>
                l.StartsWith("P<") ||
                l.StartsWith("PN") ||
                l.StartsWith("PNSYR"));

            string? line2 = null;

            if (line1 != null)
            {
                int index = lines.IndexOf(line1);

                if (index >= 0 && index + 1 < lines.Count)
                {
                    line2 = lines[index + 1];
                }
            }

            if (line2 == null)
            {
                line2 = lines.FirstOrDefault(l =>
                    l.Length >= 35 &&
                    Regex.IsMatch(l, @"^[A-Z0-9<]{25,}$") &&
                    !l.StartsWith("P"));
            }

            if (line1 == null || line2 == null)
                return null;

            string fullName = ExtractNameFromMrzLine1(line1);

            string passportNumber = "";
            string nationality = "";
            string gender = "";
            DateTime? dob = null;
            DateTime? expiry = null;

            if (line2.Length >= 9)
            {
                passportNumber = CleanMrzValue(line2.Substring(0, 9));
            }

            if (line2.Length >= 13)
            {
                nationality = line2.Substring(10, 3).Replace("<", "");
            }

            if (line2.Length >= 19)
            {
                string dobRaw = line2.Substring(13, 6);
                dob = ParseMrzDate(dobRaw, true);
            }

            if (line2.Length >= 21)
            {
                gender = line2.Substring(20, 1).Replace("<", "");
            }

            if (line2.Length >= 27)
            {
                string expRaw = line2.Substring(21, 6);
                expiry = ParseMrzDate(expRaw, false);
            }

            return new MrzResult
            {
                PassportNumber = passportNumber,
                FullName = fullName,
                Nationality = nationality,
                Gender = gender,
                DateOfBirth = dob,
                PassportExpiryDate = expiry
            };
        }

        private static string NormalizeMrzLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            string cleaned = line.ToUpperInvariant()
                .Replace(" ", "")
                .Replace("«", "<")
                .Replace("‹", "<")
                .Replace(">", "<");

            cleaned = Regex.Replace(cleaned, @"[^A-Z0-9<]", "");

            return cleaned;
        }

        private static string ExtractNameFromMrzLine1(string line1)
        {
            int doubleSeparatorIndex = line1.IndexOf("<<", StringComparison.Ordinal);

            if (doubleSeparatorIndex < 0)
            {
                string fallback = line1.Length > 5
                    ? line1.Substring(5)
                    : line1;

                return CleanName(fallback);
            }

            string beforeSeparator = line1.Substring(0, doubleSeparatorIndex);
            string afterSeparator = line1.Substring(doubleSeparatorIndex + 2);

            string surname = beforeSeparator.Length > 5
                ? beforeSeparator.Substring(5)
                : beforeSeparator;

            string givenNames = afterSeparator;

            return CleanName($"{givenNames} {surname}");
        }

        private static DateTime? ParseMrzDate(string value, bool isBirthDate)
        {
            if (value.Length != 6 || !value.All(char.IsDigit))
                return null;

            int yy = int.Parse(value.Substring(0, 2));
            int mm = int.Parse(value.Substring(2, 2));
            int dd = int.Parse(value.Substring(4, 2));

            int currentYearTwoDigits = DateTime.UtcNow.Year % 100;
            int year;

            if (isBirthDate)
            {
                year = yy > currentYearTwoDigits
                    ? 1900 + yy
                    : 2000 + yy;
            }
            else
            {
                year = 2000 + yy;
            }

            try
            {
                return new DateTime(year, mm, dd);
            }
            catch
            {
                return null;
            }
        }

        private static string CleanMrzValue(string value)
        {
            return value.Replace("<", "").Trim();
        }

        private static string CleanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Replace("<", " ");
            value = Regex.Replace(value, @"[^A-Za-z\s]", " ");
            value = Regex.Replace(value, @"\s+", " ").Trim();

            return value.ToUpperInvariant();
        }

        private static string CleanPassportNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.ToUpperInvariant();
            value = Regex.Replace(value, @"[^A-Z0-9]", "");

            return value;
        }

        private static string NormalizeNationality(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.ToUpperInvariant()
                .Replace("0", "O")
                .Replace("1", "I");

            if (value.Contains("YR") || value.Contains("SY"))
                return "SYR";

            return value;
        }

        private static string NormalizeGender(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.ToUpperInvariant();

            if (value.Contains("M"))
                return "M";

            if (value.Contains("F"))
                return "F";

            return value;
        }

        private static bool ShouldUseMrzPassportNumber(string azureNumber, string mrzNumber)
        {
            if (string.IsNullOrWhiteSpace(mrzNumber))
                return false;

            if (string.IsNullOrWhiteSpace(azureNumber))
                return true;

            string cleanAzure = CleanPassportNumber(azureNumber);
            string cleanMrz = CleanPassportNumber(mrzNumber);

            if (cleanMrz.Length >= 6 && cleanMrz.Length <= 12)
                return true;

            if (cleanAzure.Length > cleanMrz.Length + 3)
                return true;

            if (cleanMrz.Length >= 6 && !cleanAzure.Contains(cleanMrz))
                return true;

            return false;
        }
    }
}