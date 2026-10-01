using System.Security;
using System.Security.Permissions;

[module: UnverifiableCode]
// Retained for Unity/Mono access to publicized game members, as required by the RoR2 modding setup.
// Modern .NET marks RequestMinimum obsolete; suppress only this compatibility attribute.
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618
[assembly: HG.Reflection.SearchableAttribute.OptInAttribute]
