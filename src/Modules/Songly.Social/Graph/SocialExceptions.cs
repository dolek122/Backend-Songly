using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Social.Graph;

internal sealed class AlreadyFriendsException()
    : ApiException(StatusCodes.Status409Conflict, "ALREADY_FRIENDS", "Użytkownicy są już znajomymi");

internal sealed class FriendRequestAlreadyHandledException()
    : ApiException(StatusCodes.Status409Conflict, "FRIEND_REQUEST_ALREADY_HANDLED", "Zaproszenie zostało już obsłużone");

internal sealed class FriendRequestAlreadyPendingException()
    : ApiException(StatusCodes.Status409Conflict, "FRIEND_REQUEST_ALREADY_PENDING", "Zaproszenie już oczekuje na odpowiedź");

/// <summary>Also for someone else's request: a 403 would confirm a relation between other people.</summary>
internal sealed class FriendRequestNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "FRIEND_REQUEST_NOT_FOUND", "Zaproszenie nie zostało znalezione");

internal sealed class FriendshipNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "FRIENDSHIP_NOT_FOUND", "Znajomość nie została znaleziona");

internal sealed class SelfActionNotAllowedException()
    : ApiException(StatusCodes.Status400BadRequest, "SELF_ACTION_NOT_ALLOWED", "Nie możesz wykonać tej akcji na własnym koncie");

internal sealed class UserBlockedException()
    : ApiException(StatusCodes.Status409Conflict, "USER_BLOCKED", "Akcja niedostępna z powodu blokady");

internal sealed class UserNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "Użytkownik nie został znaleziony");
