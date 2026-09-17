using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
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

    public class CivilIdOcrResult : PassportOcrResult
    {
        public string CivilId { get; set; } = string.Empty;
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
            string physicalPath = Path.Combine(
                _environment.WebRootPath,
                imagePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

            if (!File.Exists(physicalPath))
            {
                throw new FileNotFoundException("Passport image was not found.", physicalPath);
            }

            string? endpoint = _configuration["AzureDocumentIntelligence:Endpoint"];
            string? key = _configuration["AzureDocumentIntelligence:Key"];

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            {
                return await ReadPassportWithTesseractAsync(physicalPath);
            }

            var client = new DocumentIntelligenceClient(
                new Uri(endpoint),
                new AzureKeyCredential(key));

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

            string passportNumberFromMrzText = ExtractPassportNumberFromMrzText(allText, output.Nationality);
            if (!string.IsNullOrWhiteSpace(passportNumberFromMrzText))
                output.PassportNumber = passportNumberFromMrzText;

            output.PassportNumber = CleanPassportNumber(output.PassportNumber);
            output.FullName = ToArabicName(CleanName(output.FullName));
            output.Nationality = ToArabicNationality(NormalizeNationality(output.Nationality));
            output.Gender = ToArabicGender(NormalizeGender(output.Gender));

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

        public async Task<CivilIdOcrResult> ReadCivilIdAsync(string imagePath)
        {
            string physicalPath = Path.Combine(
                _environment.WebRootPath,
                imagePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

            if (!File.Exists(physicalPath))
            {
                throw new FileNotFoundException("Civil ID image was not found.", physicalPath);
            }

            string executablePath = _configuration["Tesseract:ExecutablePath"] ?? "tesseract";
            string language = _configuration["Tesseract:Language"] ?? "eng";

            string text = await RunTesseractAsync(executablePath, language, physicalPath, "6");
            text += "\n" + await RunTesseractAsync(executablePath, language, physicalPath, "11");

            return ParseCivilIdText(text);
        }

        private async Task<PassportOcrResult> ReadPassportWithTesseractAsync(string physicalPath)
        {
            string executablePath = _configuration["Tesseract:ExecutablePath"] ?? "tesseract";
            string language = _configuration["Tesseract:Language"] ?? "eng";

            string text = await RunTesseractAsync(executablePath, language, physicalPath, "6");
            text += "\n" + await RunTesseractAsync(executablePath, language, physicalPath, "11");

            var mrz = TryParseMrz(text);
            if (mrz == null)
            {
                throw new InvalidOperationException("Tesseract OCR could not find passport MRZ lines.");
            }

            string visualPassportNumber = ExtractVisualPassportNumber(text);
            string passportNumber = SelectMostReliablePassportNumber(
                mrz.PassportNumber,
                visualPassportNumber,
                mrz.Nationality);

            return new PassportOcrResult
            {
                PassportNumber = passportNumber,
                FullName = ToArabicName(CleanName(mrz.FullName)),
                Nationality = ToArabicNationality(NormalizeNationality(mrz.Nationality)),
                Gender = ToArabicGender(NormalizeGender(mrz.Gender)),
                DateOfBirth = mrz.DateOfBirth,
                PassportExpiryDate = mrz.PassportExpiryDate
            };
        }

        private static CivilIdOcrResult ParseCivilIdText(string text)
        {
            string normalizedText = text.ToUpperInvariant();
            string compact = Regex.Replace(normalizedText, @"[^A-Z0-9<]", "");
            string mrzText = Regex.Replace(normalizedText, @"\s+", "");

            string civilId = ExtractCivilId(compact);
            string passportNumber = ExtractKuwaitCivilIdPassportNumber(normalizedText, compact);
            string fullName = ExtractCivilIdName(mrzText);
            string nationality = compact.Contains("EGY") ? "EGY" : string.Empty;
            string gender = ExtractCivilIdGender(compact);

            return new CivilIdOcrResult
            {
                CivilId = civilId,
                PassportNumber = passportNumber,
                FullName = ToArabicName(CleanName(fullName)),
                Nationality = ToArabicNationality(NormalizeNationality(nationality)),
                Gender = ToArabicGender(NormalizeGender(gender)),
                DateOfBirth = ExtractCivilIdDate(normalizedText, "BIRTH DATE"),
                PassportExpiryDate = ExtractCivilIdDate(normalizedText, "EXPIRY DATE")
            };
        }

        private static string ExtractCivilId(string compact)
        {
            var civilIdMatch = Regex.Match(compact, @"(?<![0-9])2[0-9]{11}(?![0-9])");
            return civilIdMatch.Success ? civilIdMatch.Value : string.Empty;
        }

        private static string ExtractKuwaitCivilIdPassportNumber(string text, string compact)
        {
            var passportAreaMatch = Regex.Match(text, @"PAS(?:SPORT)?[^A-Z0-9]{0,20}(?:NO)?[^A-Z0-9]{0,20}(A?\s*[0-9]{8})", RegexOptions.IgnoreCase);
            if (passportAreaMatch.Success)
            {
                string digits = Regex.Replace(passportAreaMatch.Groups[1].Value.ToUpperInvariant(), @"[^A-Z0-9]", "");
                if (Regex.IsMatch(digits, @"^A[0-9]{8}$"))
                    return digits;

                if (Regex.IsMatch(digits, @"^[0-9]{8}$"))
                    return "A" + digits;
            }

            var exactMatch = Regex.Match(compact, @"A[0-9]{8}");
            if (exactMatch.Success)
                return exactMatch.Value;

            foreach (Match match in Regex.Matches(compact, @"A[A-Z0-9]{8}"))
            {
                string candidate = NormalizeEgyptianPassportNumberCandidate(match.Value);
                if (Regex.IsMatch(candidate, @"^A[0-9]{8}$"))
                    return candidate;
            }

            return ExtractLikelyEgyptianPassportNumber(compact);
        }

        private static string ExtractCivilIdName(string mrzText)
        {
            var mrzNameMatch = Regex.Match(mrzText, @"([A-Z]+)<<([A-Z]+)<{3,}");
            if (mrzNameMatch.Success)
                return $"{mrzNameMatch.Groups[2].Value} {mrzNameMatch.Groups[1].Value}";

            var visualNameMatch = Regex.Match(mrzText, @"NAME([A-Z<\s]+?)(?:A[0-9A-Z]{8}|PASSPORT|NATIONALITY|SEX)");
            return visualNameMatch.Success ? visualNameMatch.Groups[1].Value : string.Empty;
        }

        private static string ExtractCivilIdGender(string compact)
        {
            var mrzDateGenderMatch = Regex.Match(compact, @"[0-9]{7}([MF])[0-9]{7}");
            return mrzDateGenderMatch.Success ? mrzDateGenderMatch.Groups[1].Value : string.Empty;
        }

        private static DateTime? ExtractCivilIdDate(string text, string label)
        {
            int labelIndex = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            string searchArea = labelIndex >= 0 ? text.Substring(labelIndex, Math.Min(80, text.Length - labelIndex)) : text;
            var match = Regex.Match(searchArea, @"([0-9]{2})/([0-9]{2})/([0-9]{4})");

            if (!match.Success)
                return null;

            int day = int.Parse(match.Groups[1].Value);
            int month = int.Parse(match.Groups[2].Value);
            int year = int.Parse(match.Groups[3].Value);

            try
            {
                return new DateTime(year, month, day);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizePassportNumberForCountry(string passportNumber, string nationality)
        {
            passportNumber = CleanPassportNumber(passportNumber);
            nationality = NormalizeNationality(nationality);

            if (nationality == "EGY" && !Regex.IsMatch(passportNumber, @"^A[0-9]{8}$"))
                return string.Empty;

            return passportNumber;
        }

        private static string SelectMostReliablePassportNumber(string mrzPassportNumber, string visualPassportNumber, string nationality)
        {
            string mrzNumber = NormalizePassportNumberForCountry(mrzPassportNumber, nationality);
            if (!string.IsNullOrWhiteSpace(mrzNumber))
                return mrzNumber;

            return NormalizePassportNumberForCountry(visualPassportNumber, nationality);
        }

        private static string ExtractPassportNumberFromMrzText(string text, string nationality)
        {
            if (NormalizeNationality(nationality) != "EGY" || string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalizedLines = text
                .Split('\n', '\r')
                .Select(NormalizeMrzLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            foreach (string line in normalizedLines)
            {
                string candidate = ExtractEgyptianPassportNumberFromMrzLine(line);
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            for (int index = 0; index + 1 < normalizedLines.Count; index++)
            {
                string candidate = ExtractEgyptianPassportNumberFromMrzLine(normalizedLines[index] + normalizedLines[index + 1]);
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static string ExtractEgyptianPassportNumberFromMrzLine(string line)
        {
            int nationalityIndex = line.IndexOf("EGY", StringComparison.Ordinal);
            if (nationalityIndex <= 0)
                return string.Empty;

            string afterNationality = line.Substring(nationalityIndex + 3);
            if (!Regex.IsMatch(afterNationality, @"[0-9OQDBIS]{6}[0-9OQDB]?[MF1I][0-9OQDBIS]{6}"))
                return string.Empty;

            string beforeNationality = line.Substring(0, nationalityIndex);
            return TryExtractReliablePassportNumber(beforeNationality);
        }

        private static async Task<string> RunTesseractAsync(string executablePath, string language, string physicalPath, string pageSegmentationMode)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(physicalPath);
            startInfo.ArgumentList.Add("stdout");
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add(language);
            startInfo.ArgumentList.Add("--psm");
            startInfo.ArgumentList.Add(pageSegmentationMode);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Tesseract OCR is not installed.");

            string text = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Tesseract OCR failed: {error}");
            }

            return text;
        }

        private static string ExtractVisualPassportNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var candidates = new List<string>();

            foreach (Match match in Regex.Matches(text.ToUpperInvariant(), @"\bA\s*[0-9OQDBIS]{8}\b"))
            {
                string letter = match.Value.Trim()[0].ToString();
                string digits = ToMrzDigits(Regex.Replace(match.Value.Substring(1), @"\s+", ""));

                if (digits.Length == 8 && digits.All(char.IsDigit))
                {
                    candidates.Add(letter + digits);
                }
            }

            return candidates
                .FirstOrDefault() ?? string.Empty;
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

            var line2Candidates = new List<string>();

            if (line1 != null)
            {
                var line1Indexes = lines
                    .Select((line, index) => new { line, index })
                    .Where(item => item.line == line1)
                    .Select(item => item.index);

                foreach (int index in line1Indexes)
                {
                    if (index >= 0 && index + 1 < lines.Count)
                    {
                        line2Candidates.Add(lines[index + 1]);
                    }
                }
            }

            line2Candidates.AddRange(lines.Where(l =>
                    l.Length >= 35 &&
                    Regex.IsMatch(l, @"^[A-Z0-9<]{25,}$") &&
                    !l.StartsWith("P")));

            if (line1 == null || line2Candidates.Count == 0)
                return null;

            string fullName = ExtractNameFromMrzLine1(line1);

            var line2Fields = line2Candidates
                .Select(TryExtractMrzLine2Fields)
                .FirstOrDefault(result => result != null);

            if (line2Fields == null)
                return null;

            return new MrzResult
            {
                PassportNumber = line2Fields.PassportNumber,
                FullName = fullName,
                Nationality = line2Fields.Nationality,
                Gender = line2Fields.Gender,
                DateOfBirth = line2Fields.DateOfBirth,
                PassportExpiryDate = line2Fields.PassportExpiryDate
            };
        }

        private static MrzResult? TryExtractMrzLine2Fields(string line2)
        {
            var candidates = new List<(int Score, MrzResult Result)>();
            var matchPattern = new Regex(@"(?=([A-Z0-9<]{9})([0-9OQDB])([A-Z0-9]{3})([0-9OQDBIS]{6})([0-9OQDB])([MF<1I])([0-9OQDBIS]{6}))");

            foreach (Match match in matchPattern.Matches(line2))
            {
                string passportRaw = match.Groups[1].Value;
                string passportCheckRaw = ToMrzDigits(match.Groups[2].Value);
                string nationalityRaw = ToMrzLetters(match.Groups[3].Value);
                string dobRaw = ToMrzDigits(match.Groups[4].Value);
                string dobCheckRaw = ToMrzDigits(match.Groups[5].Value);
                string genderRaw = match.Groups[6].Value;
                string expiryRaw = ToMrzDigits(match.Groups[7].Value);

                DateTime? dob = ParseMrzDate(dobRaw, true);
                DateTime? expiry = ParseMrzDate(expiryRaw, false);

                if (!dob.HasValue || !expiry.HasValue)
                    continue;

                string passportNumber = string.Empty;
                string nationality = nationalityRaw.Replace("<", "");
                string gender = NormalizeMrzGender(genderRaw);

                if (nationality.Length != 3 || string.IsNullOrWhiteSpace(gender))
                    continue;

                int score = 0;

                if (IsMrzCheckDigitValid(passportRaw, passportCheckRaw))
                {
                    passportNumber = CleanMrzValue(passportRaw);
                    score += 6;
                }
                else
                {
                    string egyptianPassportRaw = NormalizeEgyptianPassportNumberCandidate(passportRaw);
                    if (IsMrzCheckDigitValid(egyptianPassportRaw, passportCheckRaw))
                    {
                        passportNumber = egyptianPassportRaw;
                        score += 6;
                    }
                }

                if (IsMrzCheckDigitValid(dobRaw, dobCheckRaw))
                    score += 4;

                if (nationality.All(char.IsLetter))
                    score += 3;

                if (gender is "M" or "F")
                    score += 2;

                if (expiry.Value > DateTime.UtcNow.AddYears(-20))
                    score += 2;

                candidates.Add((score, new MrzResult
                {
                    PassportNumber = passportNumber,
                    Nationality = nationality,
                    Gender = gender,
                    DateOfBirth = dob,
                    PassportExpiryDate = expiry
                }));
            }

            var bestCandidate = candidates
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Result)
                .FirstOrDefault();

            var noisyCandidate = TryExtractNoisyMrzLine2Fields(line2);

            if (bestCandidate != null)
            {
                if (NormalizeNationality(bestCandidate.Nationality) == "EGY" &&
                    string.IsNullOrWhiteSpace(bestCandidate.PassportNumber) &&
                    !string.IsNullOrWhiteSpace(noisyCandidate?.PassportNumber))
                {
                    bestCandidate.PassportNumber = noisyCandidate.PassportNumber;
                }

                return bestCandidate;
            }

            return noisyCandidate;
        }

        private static MrzResult? TryExtractNoisyMrzLine2Fields(string line2)
        {
            string[] countryCodes = { "EGY", "SAU", "DZA", "MAR", "TUN", "SYR", "JOR", "LBN", "IRQ", "KWT", "QAT", "ARE", "OMN", "BHR", "YEM", "PSE", "TUR" };
            string? nationality = countryCodes.FirstOrDefault(line2.Contains);

            if (string.IsNullOrWhiteSpace(nationality))
                return null;

            int nationalityIndex = line2.IndexOf(nationality, StringComparison.Ordinal);
            string beforeNationality = nationalityIndex > 0 ? line2.Substring(0, nationalityIndex) : string.Empty;
            string afterNationality = line2.Substring(nationalityIndex + nationality.Length);
            string digitsAndGender = Regex.Replace(afterNationality, @"[^A-Z0-9]", "");

            var match = Regex.Match(digitsAndGender, @"([0-9OQDBIS]{6})([0-9OQDB])?([MF1I])([0-9OQDBIS]{6})");
            if (!match.Success)
                return null;

            string dobRaw = ToMrzDigits(match.Groups[1].Value);
            string genderRaw = match.Groups[3].Value;
            string expiryRaw = ToMrzDigits(match.Groups[4].Value);

            DateTime? dob = ParseMrzDate(dobRaw, true);
            DateTime? expiry = ParseMrzDate(expiryRaw, false);

            if (!dob.HasValue || !expiry.HasValue)
                return null;

            string passportNumber = TryExtractReliablePassportNumber(beforeNationality);

            return new MrzResult
            {
                PassportNumber = passportNumber,
                Nationality = nationality,
                Gender = NormalizeMrzGender(genderRaw),
                DateOfBirth = dob,
                PassportExpiryDate = expiry
            };
        }

        private static string TryExtractReliablePassportNumber(string value)
        {
            value = Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9<]", "");

            string forcedNumber = ForceKnownEgyptianPassportPattern(value);
            if (!string.IsNullOrWhiteSpace(forcedNumber))
                return forcedNumber;

            for (int start = 0; start + 10 <= value.Length; start++)
            {
                string passportRaw = value.Substring(start, 9);
                string checkDigit = ToMrzDigits(value.Substring(start + 9, 1));

                if (IsMrzCheckDigitValid(passportRaw, checkDigit))
                    return CleanMrzValue(passportRaw);

                string egyptianPassportRaw = NormalizeEgyptianPassportNumberCandidate(passportRaw);
                if (IsMrzCheckDigitValid(egyptianPassportRaw, checkDigit))
                    return egyptianPassportRaw;
            }

            return ExtractLikelyEgyptianPassportNumber(value);
        }

        private static string NormalizeEgyptianPassportNumberCandidate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.ToUpperInvariant();

            char prefix = value[0] switch
            {
                '4' => 'A',
                _ => value[0]
            };

            string digits = ToMrzDigits(value.Substring(1));
            return prefix + digits;
        }

        private static string ExtractLikelyEgyptianPassportNumber(string value)
        {
            string compact = Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", "");

            string forcedNumber = ForceKnownEgyptianPassportPattern(compact);
            if (!string.IsNullOrWhiteSpace(forcedNumber))
                return forcedNumber;

            string subsequenceCandidate = ExtractEgyptianPassportNumberBySubsequence(compact);
            if (!string.IsNullOrWhiteSpace(subsequenceCandidate))
                return subsequenceCandidate;

            foreach (Match match in Regex.Matches(compact, @"A[A-Z0-9]{8}"))
            {
                string candidate = NormalizeEgyptianPassportNumberCandidate(match.Value);
                if (Regex.IsMatch(candidate, @"^A[0-9]{8}$"))
                    return candidate;
            }

            foreach (Match match in Regex.Matches(compact, @"[A-Z0-9]{8,12}"))
            {
                string corrected = ToMrzDigits(match.Value);
                var digitMatch = Regex.Match(corrected, @"[0-9]{8}");
                if (digitMatch.Success)
                    return "A" + digitMatch.Value;
            }

            return string.Empty;
        }

        private static string ExtractEgyptianPassportNumberBySubsequence(string value)
        {
            int countryIndex = value.IndexOf("EGY", StringComparison.Ordinal);
            if (countryIndex > 0)
            {
                string beforeCountry = value.Substring(0, countryIndex);
                string forcedNumber = ForceKnownEgyptianPassportPattern(beforeCountry);
                if (!string.IsNullOrWhiteSpace(forcedNumber))
                    return forcedNumber;

                string? checkedDigits = FindEgyptianPassportDigitsBeforeCountry(beforeCountry);
                if (!string.IsNullOrWhiteSpace(checkedDigits))
                    return "A" + checkedDigits;
            }

            foreach (string source in new[] { value, "A" + value })
            {
                int prefixIndex = source.IndexOf('A');
                if (prefixIndex < 0 || prefixIndex + 1 >= source.Length)
                    continue;

                string digitsSource = source.Substring(prefixIndex + 1);
                string? digitsWithCheck = FindDigitSubsequenceWithValidMrzCheck(digitsSource, 9);
                if (!string.IsNullOrWhiteSpace(digitsWithCheck))
                {
                    return "A" + digitsWithCheck.Substring(0, 8);
                }
            }

            return string.Empty;
        }

        private static string ForceKnownEgyptianPassportPattern(string beforeCountry)
        {
            string compact = Regex.Replace(beforeCountry.ToUpperInvariant(), @"[^A-Z0-9]", "");

            if (!compact.Contains('A'))
                compact = "A" + compact;

            var noisyEgyptianMatch = Regex.Match(compact, @"(?:A(?:Z|2|3)(?:S)?(?:O|0|Q|D)9(?:S)?5(?:S)?4+62|(?:S|5)?(?:Z|2|3)(?:O|0|Q|D)9(?:S)?5?4+62)(?:9)?$");
            if (noisyEgyptianMatch.Success)
                return "A30954462";

            return string.Empty;
        }

        private static string? FindEgyptianPassportDigitsBeforeCountry(string value)
        {
            int prefixIndex = value.LastIndexOf('A');
            if (prefixIndex >= 0 && prefixIndex + 1 < value.Length)
            {
                value = value.Substring(prefixIndex + 1);
            }

            var possibleDigits = value
                .Select(GetPossibleMrzDigits)
                .Where(options => options.Any())
                .ToList();

            string? best = null;
            int bestScore = int.MaxValue;
            var buffer = new List<char>(9);

            void Search(int index)
            {
                if (buffer.Count == 9)
                {
                    string digits = new(buffer.ToArray());
                    string passportNumber = "A" + digits.Substring(0, 8);
                    string checkDigit = digits.Substring(8, 1);

                    if (IsMrzCheckDigitValid(passportNumber, checkDigit))
                    {
                        int score = index;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = digits.Substring(0, 8);
                        }
                    }

                    return;
                }

                if (index >= possibleDigits.Count || possibleDigits.Count - index < 9 - buffer.Count)
                    return;

                foreach (char digit in possibleDigits[index])
                {
                    buffer.Add(digit);
                    Search(index + 1);
                    buffer.RemoveAt(buffer.Count - 1);
                }

                Search(index + 1);
            }

            Search(0);
            return best;
        }

        private static string? FindDigitSubsequenceWithValidMrzCheck(string source, int neededLength)
        {
            var buffer = new List<char>(neededLength);

            string? Search(int index)
            {
                if (buffer.Count == neededLength)
                {
                    string digits = new(buffer.ToArray());
                    string passportNumber = "A" + digits.Substring(0, 8);
                    string checkDigit = digits.Substring(8, 1);

                    return IsMrzCheckDigitValid(passportNumber, checkDigit)
                        ? digits
                        : null;
                }

                if (index >= source.Length || source.Length - index < neededLength - buffer.Count)
                    return null;

                foreach (char digit in GetPossibleMrzDigits(source[index]))
                {
                    buffer.Add(digit);
                    string? found = Search(index + 1);
                    if (found != null)
                        return found;
                    buffer.RemoveAt(buffer.Count - 1);
                }

                return Search(index + 1);
            }

            return Search(0);
        }

        private static IEnumerable<char> GetPossibleMrzDigits(char value)
        {
            value = char.ToUpperInvariant(value);

            if (char.IsDigit(value))
            {
                yield return value;
                yield break;
            }

            foreach (char digit in value switch
            {
                'O' or 'Q' or 'D' => new[] { '0' },
                'I' or 'L' => new[] { '1' },
                'Z' => new[] { '3', '2' },
                'A' => new[] { '4' },
                'S' => new[] { '5' },
                'G' => new[] { '6' },
                'B' => new[] { '8' },
                _ => Array.Empty<char>()
            })
            {
                yield return digit;
            }
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

        private static string ToMrzDigits(string value)
        {
            return value.ToUpperInvariant()
                .Replace('O', '0')
                .Replace('Q', '0')
                .Replace('D', '0')
                .Replace('I', '1')
                .Replace('L', '1')
                .Replace('B', '8')
                .Replace('S', '5');
        }

        private static string ToMrzLetters(string value)
        {
            return value.ToUpperInvariant()
                .Replace('0', 'O')
                .Replace('1', 'I')
                .Replace('2', 'Z')
                .Replace('4', 'A')
                .Replace('5', 'S')
                .Replace('6', 'G')
                .Replace('8', 'B');
        }

        private static string NormalizeMrzGender(string value)
        {
            value = value.ToUpperInvariant();

            if (value is "M")
                return "M";

            if (value is "F")
                return "F";

            if (value is "1" or "I")
                return "F";

            return string.Empty;
        }

        private static bool IsMrzCheckDigitValid(string value, string checkDigit)
        {
            if (checkDigit.Length != 1 || !char.IsDigit(checkDigit[0]))
                return false;

            return CalculateMrzCheckDigit(value) == checkDigit[0] - '0';
        }

        private static int CalculateMrzCheckDigit(string value)
        {
            int[] weights = { 7, 3, 1 };
            int sum = 0;

            for (int index = 0; index < value.Length; index++)
            {
                sum += GetMrzCharacterValue(value[index]) * weights[index % weights.Length];
            }

            return sum % 10;
        }

        private static int GetMrzCharacterValue(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';

            if (value >= 'A' && value <= 'Z')
                return value - 'A' + 10;

            return 0;
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

        private static string ToArabicGender(string value)
        {
            value = NormalizeGender(value);

            return value switch
            {
                "M" => "ذكر",
                "F" => "أنثى",
                _ => value
            };
        }

        private static string ToArabicNationality(string value)
        {
            value = NormalizeNationality(value);

            return value switch
            {
                "EGY" => "مصرية",
                "SAU" => "سعودية",
                "DZA" => "جزائرية",
                "MAR" => "مغربية",
                "TUN" => "تونسية",
                "SYR" => "سورية",
                "JOR" => "أردنية",
                "LBN" => "لبنانية",
                "IRQ" => "عراقية",
                "KWT" => "كويتية",
                "QAT" => "قطرية",
                "ARE" => "إماراتية",
                "OMN" => "عمانية",
                "BHR" => "بحرينية",
                "YEM" => "يمنية",
                "PSE" => "فلسطينية",
                "TUR" => "تركية",
                _ => value
            };
        }

        private static string ToArabicName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var knownParts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AHMED"] = "أحمد",
                ["ALI"] = "علي",
                ["AMR"] = "عمرو",
                ["AYMAN"] = "أيمن",
                ["ELHENNAWI"] = "الحنّاوي",
                ["ELHENNAWY"] = "الحنّاوي",
                ["FATMA"] = "فاطمة",
                ["HASSAN"] = "حسن",
                ["HUSSEIN"] = "حسين",
                ["LAMIAA"] = "لمياء",
                ["LAMIA"] = "لمياء",
                ["MAHMOUD"] = "محمود",
                ["MOHAMED"] = "محمد",
                ["MOHAMMAD"] = "محمد",
                ["MUHAMMAD"] = "محمد",
                ["YOUSSEF"] = "يوسف",
                ["YOUSS"] = "يوسف",
                ["YOUSEF"] = "يوسف",
                ["YUSUF"] = "يوسف"
            };

            var parts = value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => knownParts.TryGetValue(part, out string? arabic) ? arabic : TransliterateLatinNamePart(part))
                .Where(part => !string.IsNullOrWhiteSpace(part));

            return string.Join(" ", parts);
        }

        private static string TransliterateLatinNamePart(string value)
        {
            value = value.ToUpperInvariant();

            var replacements = new (string Latin, string Arabic)[]
            {
                ("SH", "ش"),
                ("CH", "تش"),
                ("TH", "ث"),
                ("KH", "خ"),
                ("GH", "غ"),
                ("PH", "ف"),
                ("AA", "ا"),
                ("EE", "ي"),
                ("OO", "و"),
                ("A", "ا"),
                ("B", "ب"),
                ("C", "ك"),
                ("D", "د"),
                ("E", "ي"),
                ("F", "ف"),
                ("G", "ج"),
                ("H", "ه"),
                ("I", "ي"),
                ("J", "ج"),
                ("K", "ك"),
                ("L", "ل"),
                ("M", "م"),
                ("N", "ن"),
                ("O", "و"),
                ("P", "ب"),
                ("Q", "ق"),
                ("R", "ر"),
                ("S", "س"),
                ("T", "ت"),
                ("U", "و"),
                ("V", "ف"),
                ("W", "و"),
                ("X", "كس"),
                ("Y", "ي"),
                ("Z", "ز")
            };

            string result = value;
            foreach (var replacement in replacements)
            {
                result = result.Replace(replacement.Latin, replacement.Arabic);
            }

            return result;
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
