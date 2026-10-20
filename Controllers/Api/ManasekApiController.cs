using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    /// <summary>
    /// Manasek (Umrah guide) content shown step by step in the mobile app.
    /// Stored in App_Data/manasek-content.json so Ruwad can edit it without a new app release.
    /// </summary>
    [ApiController]
    [Route("api/manasek")]
    public class ManasekApiController : ControllerBase
    {
        public static readonly IReadOnlySet<string> AllowedTools = new HashSet<string> { "tawaf-counter", "sai-counter", "halq-choice" };
        public static readonly IReadOnlySet<string> AllowedBlocks = new HashSet<string> { "checklist", "checkgrid", "attire", "notes", "list", "intention", "recitation", "tip", "success" };

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private static readonly SemaphoreSlim WriteLock = new(1, 1);
        private readonly string _filePath;

        public ManasekApiController(IWebHostEnvironment environment)
        {
            _filePath = Path.Combine(environment.ContentRootPath, "App_Data", "manasek-content.json");
        }

        [HttpGet]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<ManasekContentDto>> Get() => Ok(await ReadOrCreateAsync());

        [Authorize(Roles = "Admin")]
        [HttpPut]
        public async Task<ActionResult<ManasekContentDto>> Update([FromBody] ManasekContentDto request)
        {
            var error = Validate(request);
            if (error != null) return BadRequest(new { message = error });

            var content = request with
            {
                Version = Math.Max(1, (await ReadOrCreateAsync()).Version + 1),
                UpdatedAt = DateTime.UtcNow,
                Steps = request.Steps.OrderBy(step => step.Order).ToList()
            };
            await WriteAsync(content);
            return Ok(content);
        }

        public static string? Validate(ManasekContentDto content)
        {
            if (content.Steps.Count == 0) return "أضف خطوة واحدة على الأقل.";
            if (content.Types.Count == 0) return "أضف نوع مناسك واحداً على الأقل.";
            if (content.Steps.Select(step => step.Id).Distinct().Count() != content.Steps.Count) return "معرفات الخطوات يجب أن تكون فريدة.";
            var typeIds = content.Types.Select(type => type.Id).ToHashSet();
            foreach (var step in content.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Id) || string.IsNullOrWhiteSpace(step.Title)) return "لكل خطوة معرف وعنوان.";
                if (step.Tool != null && !AllowedTools.Contains(step.Tool)) return $"الأداة {step.Tool} غير مدعومة في التطبيق.";
                if (step.AppliesTo.Any(type => !typeIds.Contains(type))) return $"الخطوة {step.Title} مرتبطة بنوع غير موجود.";
                var pages = step.Pages ?? Array.Empty<ManasekPageDto>();
                if (pages.Select(page => page.Id).Distinct().Count() != pages.Count) return $"صفحات الخطوة {step.Title} يجب أن تكون بمعرفات فريدة.";
                foreach (var page in pages)
                {
                    if (string.IsNullOrWhiteSpace(page.Id) || string.IsNullOrWhiteSpace(page.Title) || page.Blocks.Count == 0) return $"كل صفحة في {step.Title} تحتاج معرفاً وعنواناً ومحتوى.";
                    var unknown = page.Blocks.FirstOrDefault(block => !AllowedBlocks.Contains(block.Kind));
                    if (unknown != null) return $"نوع القسم {unknown.Kind} غير مدعوم في التطبيق.";
                }
            }
            return null;
        }

        private async Task<ManasekContentDto> ReadOrCreateAsync()
        {
            if (System.IO.File.Exists(_filePath))
            {
                try
                {
                    await using var stream = System.IO.File.OpenRead(_filePath);
                    var content = await JsonSerializer.DeserializeAsync<ManasekContentDto>(stream, JsonOptions);
                    // Version 1 is the untouched default: refresh it so new default screens (pages) reach the app.
                    // Any admin edit bumps the version, and edited content is never overwritten.
                    if (content != null && Validate(content) == null && !(content.Version <= 1 && content.Steps.All(step => step.Pages == null)))
                        return content;
                }
                catch (JsonException)
                {
                    // A broken file falls back to the default guide below instead of breaking the app.
                }
            }

            var defaults = BuildDefault();
            if (!System.IO.File.Exists(_filePath)) await WriteAsync(defaults);
            return defaults;
        }

        private async Task WriteAsync(ManasekContentDto content)
        {
            await WriteLock.WaitAsync();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                await using var stream = System.IO.File.Create(_filePath);
                await JsonSerializer.SerializeAsync(stream, content, JsonOptions);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        /// <summary>The four Ihram screens of Ruwad's design: preparation, clothing, intention and talbiya, prohibitions.</summary>
        private static IReadOnlyList<ManasekPageDto> IhramPages() => new List<ManasekPageDto>
        {
            new("preparation", "الاستعداد للإحرام", "تهيأ بخشوع لتبدأ رحلتك المباركة", "/landingpage/hero-kaaba-premium.png", "تم الاستعداد", new List<ManasekBlockDto>
            {
                new("checklist", "قبل أن تُحرم", "جهز نفسك بدنياً ومعنوياً قبل نية الإحرام", "clipboard-outline", Items: new List<ManasekItemDto>
                {
                    new("الاغتسال", "للنظافة والاستعداد للطاعة", "water-outline"),
                    new("تقليم الأظافر", "من سنن الفطرة والنظافة", "cut-outline"),
                    new("التطيب للرجال قبل النية", "يُستحب التطيب قبل الإحرام للرجال", "flask-outline"),
                    new("إزالة ما يحتاج من الشعر", "كالشعر الزائد تحت الإبطين وما شابه", "cut-outline"),
                    new("التأكد من الميقات", "معرفة الميقات حسب موقعك وطريقك", "location-outline")
                }),
                new("checkgrid", "قائمة التجهيز", "أهم المستلزمات التي ستحتاجها في هذه المرحلة", "briefcase-outline", Items: new List<ManasekItemDto>
                {
                    new("سجادة صغيرة", Icon: "albums-outline"),
                    new("زجاجة ماء", Icon: "water-outline"),
                    new("حقيبة خفيفة", Icon: "bag-handle-outline"),
                    new("مظلة", Icon: "umbrella-outline")
                })
            }),
            new("attire", "ملابس الإحرام", "التهيؤ لنسك العمرة", "/landingpage/kaaba.png", "الانتقال إلى النية والتلبية", new List<ManasekBlockDto>
            {
                new("attire", Groups: new List<ManasekGroupDto>
                {
                    new("للرجال", "إزار ورداء أبيضان", "/landingpage/kaaba.png", "man-outline", new List<ManasekItemDto>
                    {
                        new("إزار ورداء أبيضان", Icon: "shirt-outline"),
                        new("غير مخيطين", Icon: "close-circle-outline"),
                        new("صندل مناسب", Icon: "footsteps-outline")
                    }),
                    new("للنساء", "ببساطة واحتشام", "/landingpage/auth-mosque-bg.png", "woman-outline", new List<ManasekItemDto>
                    {
                        new("ملابس محتشمة عادية", Icon: "shirt-outline"),
                        new("دون لون محدد", Icon: "color-palette-outline"),
                        new("من دون تبرج", Icon: "sparkles-outline")
                    })
                }),
                new("notes", "ملاحظات", Icon: "document-text-outline", Items: new List<ManasekItemDto>
                {
                    new("تجنب تغطية الرأس للرجال", Icon: "man-outline"),
                    new("تغطية الوجه محل خلاف للنساء بحسب الحاجة", Icon: "alert-circle-outline"),
                    new("الراحة والستر أولاً", Icon: "heart-outline")
                })
            }),
            new("intention", "النية والتلبية", "ابدأ رحلتك بخشوع .. من الميقات", "/landingpage/auth-login-bg.png", "بدأت التلبية", new List<ManasekBlockDto>
            {
                new("intention", "من الميقات", "استحضر نية العمرة خالصةً لوجه الله تعالى", "location-outline", "/landingpage/auth-mosque-bg.png",
                    Text: "انوِ العمرة بقلبك", Highlight: "لَبَّيْكَ اللَّهُمَّ عُمْرَةً"),
                new("recitation", "ردد الآن", "واستمع إلى التلبية بصوت واضح", "pulse-outline",
                    Text: "لَبَّيْكَ اللَّهُمَّ لَبَّيْكَ، لَبَّيْكَ لَا شَرِيكَ لَكَ لَبَّيْكَ، إِنَّ الْحَمْدَ وَالنِّعْمَةَ لَكَ وَالْمُلْكَ، لَا شَرِيكَ لَكَ."),
                new("tip", "تنبيه", Icon: "bulb-outline", Text: "استمر في التلبية من وقت الإحرام وحتى بدء الطواف في المسجد الحرام.")
            }),
            new("prohibitions", "محظورات الإحرام", "لتحافظ على إحرامك وتؤدي مناسكك بطمأنينة", "/landingpage/hero-kaaba-premium.png", "الانتقال إلى الطواف", new List<ManasekBlockDto>
            {
                new("list", "محظورات الإحرام", "يجب تجنب المحظورات التالية أثناء الإحرام", "ban-outline", Items: new List<ManasekItemDto>
                {
                    new("عدم قص الشعر أو الأظافر", "يمنع قص الشعر وبعضه أو الأظافر أثناء الإحرام", "cut-outline"),
                    new("عدم استعمال الطيب بعد الإحرام", "يمنع استعمال العطور والطيب ومشتقاته", "flask-outline"),
                    new("عدم عقد النكاح أو الخطبة", "يمنع عقد النكاح أو الخطبة أو التعرض لهما", "ellipse-outline"),
                    new("تجنب الجماع ومقدماته", "يمنع الجماع وكل ما يؤدي إليه من مقدمات", "people-outline"),
                    new("اجتناب الصيد البري", "يمنع صيد الحيوانات البرية وقت الإحرام", "paw-outline"),
                    new("للرجال: عدم تغطية الرأس ولبس المخيط", "يمنع على الرجل تغطية الرأس ولبس الملابس المخيطة", "shirt-outline")
                }),
                new("success", "أنت جاهز الآن", Image: "/landingpage/kaaba.png", Text: "بعد إتمام الإحرام يمكنك التوجه إلى الطواف حول الكعبة.")
            })
        };

        /// <summary>Initial guide: the texts already shown by the mobile app, to be reviewed by Ruwad's religious advisor.</summary>
        public static ManasekContentDto BuildDefault()
        {
            var both = new[] { "single", "tamattu" };
            return new ManasekContentDto(
                1,
                DateTime.UtcNow,
                new List<ManasekTypeDto>
                {
                    new("single", "العمرة المفردة", "العمرة فقط بدون حج", "/landingpage/kaaba.png"),
                    new("tamattu", "العمرة مع الحج", "العمرة والحج معاً (التمتع)", "/landingpage/auth-login-bg.png")
                },
                new List<ManasekStepDto>
                {
                    new("ihram", 1, "الإحرام", "النية والتلبية من الميقات", "person-outline", "/landingpage/auth-mosque-bg.png",
                        new[] { "الاغتسال والتنظف", "ارتداء ملابس الإحرام البيضاء", "أداء ركعتي سنة الإحرام", "النية وقول التلبية" },
                        new List<ManasekDuaDto>
                        {
                            new("التلبية",
                                "لَبَّيْكَ اللَّهُمَّ لَبَّيْكَ، لَبَّيْكَ لَا شَرِيكَ لَكَ لَبَّيْكَ، إِنَّ الْحَمْدَ وَالنِّعْمَةَ لَكَ وَالْمُلْكَ، لَا شَرِيكَ لَكَ.",
                                "Labbaik Allahumma labbaik, labbaika la sharika laka labbaik, inna alhamda wa ni'mata laka wal-mulk, la sharika lak.")
                        },
                        new[] { "تأكد من الميقات المخصص لرحلتك مع المشرف" }, null, both, IhramPages()),
                    new("tawaf", 2, "الطواف", "سبعة أشواط حول الكعبة", "cube-outline", "/landingpage/kaaba.png",
                        new[] { "ابدأ من الحجر الأسود واستلمه إن أمكن", "طف حول الكعبة سبعة أشواط", "اجعل الكعبة على يسارك دائماً", "اقرأ الأدعية المأثورة أثناء الطواف" },
                        new List<ManasekDuaDto> { new("بين الركن اليماني والحجر الأسود", "رَبَّنَا آتِنَا فِي الدُّنْيَا حَسَنَةً وَفِي الْآخِرَةِ حَسَنَةً وَقِنَا عَذَابَ النَّارِ.", null) },
                        new[] { "سجّل كل شوط بنفسك في العداد؛ التطبيق لا يتحقق تلقائياً من اكتمال الشوط" }, "tawaf-counter", both),
                    new("sai", 3, "السعي", "بين الصفا والمروة", "walk-outline", "/landingpage/paysage.png",
                        new[] { "ابدأ من جبل الصفا", "امشِ إلى جبل المروة", "يُحتسب الذهاب شوطاً والإياب شوطاً", "أكمل سبعة أشواط" },
                        new List<ManasekDuaDto> { new("عند الصفا", "إِنَّ الصَّفَا وَالْمَرْوَةَ مِنْ شَعَائِرِ اللَّهِ.", null) },
                        new[] { "يبدأ الشوط الأول من الصفا وينتهي السابع عند المروة" }, "sai-counter", both),
                    new("halq", 4, "الحلق أو التقصير", "إنهاء مناسك العمرة", "cut-outline", "/landingpage/dormir.png",
                        new[] { "يفضل الحلق للرجال أو التقصير بإزالة جزء من الشعر", "بعد ذلك تتحلل من الإحرام", "تكون العمرة قد تمت بحمد الله" },
                        new List<ManasekDuaDto> { new("بعد إتمام العمرة", "اللَّهُمَّ تَقَبَّلْ مِنَّا وَمِنْكُمْ صَالِحَ الْأَعْمَالِ.", null) },
                        Array.Empty<string>(), "halq-choice", both)
                },
                new[] { "جواز السفر ساري المفعول والتأشيرة", "ملابس الإحرام وحذاء مريح", "رقم مشرف الرحلة وعنوان الفندق", "زجاجة ماء وأدوية شخصية" },
                new[] { "احرص على شرب الماء", "اتبع إرشادات المنظمين", "يمكنك سؤال مساعد رواد في أي وقت" },
                new ManasekCompletionDto("تمت العمرة بنجاح", "تقبل الله منا ومنكم", "﴿ رَبَّنَا تَقَبَّلْ مِنَّا إِنَّكَ أَنتَ السَّمِيعُ الْعَلِيمُ ﴾"),
                "يرجى مراجعة المحتوى الشرعي مع مرشد ديني موثوق؛ هذا الدليل للتيسير ولا يغني عن السؤال.");
        }
    }

    public sealed record ManasekContentDto(
        int Version,
        DateTime UpdatedAt,
        IReadOnlyList<ManasekTypeDto> Types,
        IReadOnlyList<ManasekStepDto> Steps,
        IReadOnlyList<string> Preparation,
        IReadOnlyList<string> Tips,
        ManasekCompletionDto Completion,
        string Disclaimer);

    public sealed record ManasekTypeDto(string Id, string Title, string Caption, string Image);

    /// <param name="Tool">Optional in-app tool for the step: tawaf-counter, sai-counter (7 rounds, to be completed before the step) or halq-choice.</param>
    /// <param name="AppliesTo">Ids of the Umrah types that include this step.</param>
    public sealed record ManasekStepDto(
        string Id,
        int Order,
        string Title,
        string Caption,
        string Icon,
        string Image,
        IReadOnlyList<string> Explanation,
        IReadOnlyList<ManasekDuaDto> Duas,
        IReadOnlyList<string> Notes,
        string? Tool,
        IReadOnlyList<string> AppliesTo,
        IReadOnlyList<ManasekPageDto>? Pages = null);

    /// <summary>A screen of a step (e.g. Ihram: preparation, clothing, intention, prohibitions). Cta is the main button label.</summary>
    public sealed record ManasekPageDto(string Id, string Title, string Subtitle, string Image, string Cta, IReadOnlyList<ManasekBlockDto> Blocks);

    /// <summary>
    /// One section of a page. Kind is one of ManasekApiController.AllowedBlocks:
    /// checklist / checkgrid (items to tick), attire (Groups), notes / list (numbered Items), intention (Text + Highlight),
    /// recitation (Text read aloud), tip (Text), success (Text + Image).
    /// </summary>
    public sealed record ManasekBlockDto(
        string Kind,
        string? Title = null,
        string? Subtitle = null,
        string? Icon = null,
        string? Image = null,
        string? Text = null,
        string? Highlight = null,
        IReadOnlyList<ManasekItemDto>? Items = null,
        IReadOnlyList<ManasekGroupDto>? Groups = null);

    public sealed record ManasekItemDto(string Title, string? Caption = null, string? Icon = null);

    public sealed record ManasekGroupDto(string Title, string Caption, string Image, string Icon, IReadOnlyList<ManasekItemDto> Items);

    public sealed record ManasekDuaDto(string Title, string Arabic, string? Transliteration);

    public sealed record ManasekCompletionDto(string Title, string Message, string Verse);
}
