namespace Songly.Common.Mail;

/// <summary>A plain-text e-mail. The body may carry a one-time token — never log it outside development.</summary>
public sealed record OutgoingEmail(string To, string Subject, string Body);
