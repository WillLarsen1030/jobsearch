using System.Text.RegularExpressions;

namespace JobSearch.Application.Matching;

public static partial class JobSignalDetector
{
    private static readonly (string Name, Regex Pattern)[] Technologies =
    [
        ("ASP.NET Core", AspNetCoreRegex()),
        ("ASP.NET", AspNetRegex()),
        ("C#", CSharpRegex()),
        (".NET", DotNetRegex()),
        ("Azure", AzureRegex()),
        ("SQL Server", SqlServerRegex()),
        ("PostgreSQL", PostgreSqlRegex()),
        ("REST/API", ApiRegex()),
        ("Microservices", MicroservicesRegex()),
        ("React", ReactRegex()),
        ("Angular", AngularRegex()),
        ("Umbraco/CMS", CmsRegex())
    ];

    public static IReadOnlyCollection<string> DetectTechnologies(string value) => Technologies
        .Where(item => item.Pattern.IsMatch(value))
        .Select(item => item.Name)
        .ToArray();

    public static bool HasRelevantEngineeringRole(string title) =>
        EngineeringRoleRegex().IsMatch(title) && !UnrelatedRoleRegex().IsMatch(title);

    public static bool IsJuniorRole(string title) => JuniorRegex().IsMatch(title);

    public static bool IsSeniorRole(string title) => SeniorRegex().IsMatch(title);

    public static bool IsBackendOrFullStackRole(string title, string description) =>
        BackendFullStackRegex().IsMatch(string.Join(' ', title, description));

    public static bool HasCompetingStackTitle(string title) => CompetingStackRegex().IsMatch(title);

    public static bool HasNonTargetSpecialization(string title) => NonTargetSpecializationRegex().IsMatch(title);

    public static bool ExplicitlyExcludesContract(string value) => NonContractRegex().IsMatch(value);

    public static bool HasPreferredEngagementLanguage(string value) => PreferredEngagementRegex().IsMatch(value);

    [GeneratedRegex(@"(?<![\w.])asp\s*\.\s*net\s+core\b|\baspnet\s+core\b", RegexOptions.IgnoreCase)]
    private static partial Regex AspNetCoreRegex();

    [GeneratedRegex(@"(?<![\w.])asp\s*\.\s*net\b|\baspnet\b", RegexOptions.IgnoreCase)]
    private static partial Regex AspNetRegex();

    [GeneratedRegex(@"(?<!\w)c\s*#(?!\w)|\bc[- ]sharp\b", RegexOptions.IgnoreCase)]
    private static partial Regex CSharpRegex();

    [GeneratedRegex(@"(?<![\w.])\.\s*net(?:\s+(?:core|framework)|\b)|\bdotnet\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetRegex();

    [GeneratedRegex(@"\b(?:microsoft\s+)?azure\b", RegexOptions.IgnoreCase)]
    private static partial Regex AzureRegex();

    [GeneratedRegex(@"\bsql\s+server\b|\bmssql\b", RegexOptions.IgnoreCase)]
    private static partial Regex SqlServerRegex();

    [GeneratedRegex(@"\bpostgres(?:ql)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex PostgreSqlRegex();

    [GeneratedRegex(@"\brest(?:ful)?\s*(?:api|service)s?\b|\bweb\s*api\b|\bapi\s+(?:design|development|integration)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ApiRegex();

    [GeneratedRegex(@"\bmicro[- ]?services?\b", RegexOptions.IgnoreCase)]
    private static partial Regex MicroservicesRegex();

    [GeneratedRegex(@"\breact(?:\.js|js)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReactRegex();

    [GeneratedRegex(@"\bangular(?:\.js|js)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex AngularRegex();

    [GeneratedRegex(@"\bumbraco\b|\bcontent management system\b|\bcms\b", RegexOptions.IgnoreCase)]
    private static partial Regex CmsRegex();

    [GeneratedRegex(@"\b(?:software|application|backend|back-end|full[- ]?stack|web|platform|api)\s+(?:developer|engineer|architect|consultant)\b|\b(?:developer|engineer|architect)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EngineeringRoleRegex();

    [GeneratedRegex(@"\b(?:marketing|sales|recruiter|account executive|product marketing|finance|payroll|legal|designer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnrelatedRoleRegex();

    [GeneratedRegex(@"\b(?:junior|entry[- ]level|intern(?:ship)?|graduate)\b|\b(?:software|application)\s+engineer\s+i\b", RegexOptions.IgnoreCase)]
    private static partial Regex JuniorRegex();

    [GeneratedRegex(@"\b(?:senior|sr\.?|lead|staff|principal|architect)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeniorRegex();

    [GeneratedRegex(@"\b(?:back[- ]?end|full[- ]?stack|server[- ]side|web\s+api)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BackendFullStackRegex();

    [GeneratedRegex(@"\b(?:contract|contractor|consulting|consultant|fractional|part[- ]time|flexible\s+(?:hours|schedule)|1099)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PreferredEngagementRegex();

    [GeneratedRegex(@"\b(?:java|python|golang|go|node(?:\.js|js)?|ruby|rails|php|rust|c\+\+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CompetingStackRegex();

    [GeneratedRegex(@"\b(?:data|analytics|machine learning|ml|ai|quality assurance|qa|test automation|salesforce|android|ios|mobile|devops|security)\s+(?:developer|engineer|architect)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonTargetSpecializationRegex();

    [GeneratedRegex(@"\bnot\s+(?:a\s+)?contract\s+(?:role|position)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonContractRegex();
}
