namespace Songly.Common.Mail;

public sealed class MailDeliveryException(string message, Exception? innerException = null) : Exception(message, innerException);
