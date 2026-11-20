using Teg.Domain.Collections;
using Teg.Domain.Commands;
using Teg.Domain.Events;
using Teg.Domain.Map;
using Teg.Domain.State;

namespace Teg.Domain.Rules;

/// <summary>
/// Trading cards for armies (R§7), only in Refuerzo. Three cards of the same symbol, or one of each symbol, are
/// worth 4, 7 and 10 armies, then 5 more every time; the counter belongs to each participant. The armies join the
/// free pool and the cards go to the discard pile.
/// </summary>
internal static class Trade
{
    /// <summary>Cards in a trade.</summary>
    internal const int Size = 3;

    /// <summary>With this many cards in hand a trade is mandatory (R§7); a valid trade always exists then.</summary>
    internal const int MandatoryFrom = 5;

    private const int FirstTradeArmies = 4;
    private const int SecondTradeArmies = 7;
    private const int ThirdTradeArmies = 10;
    private const int ArmiesAddedAfterTheThird = 5;

    /// <summary>Armies of the <paramref name="tradeNumber"/>-th trade of a participant (1 = first): 4, 7, 10, 15, 20...</summary>
    internal static int ValueOf(int tradeNumber) => tradeNumber switch
    {
        1 => FirstTradeArmies,
        2 => SecondTradeArmies,
        _ => ThirdTradeArmies + ArmiesAddedAfterTheThird * (tradeNumber - 3),
    };

    internal static bool IsMandatory(Participant participant) => participant.Cards.Count >= MandatoryFrom;

    internal static Result<Transition, RuleViolation> Apply(GameState state, TradeCards command, MapDefinition map)
    {
        var violation = CommandGuards.Check(state, command, GamePhase.Reinforcement)
            ?? ValidateCards(state.Participants[command.Seat], command.Cards, map);
        if (violation is not null)
        {
            return Result<Transition, RuleViolation>.Fail(violation);
        }

        var participant = state.Participants[command.Seat];
        var armies = ValueOf(participant.TradesDone + 1);
        var traded = command.Cards.ToHashSet();
        var afterTrade = participant with
        {
            Cards = participant.Cards.Where(card => !traded.Contains(card)).ToValueArray(),
            TradesDone = participant.TradesDone + 1,
        };

        var next = state with
        {
            Participants = state.Participants.Select(p => p.Seat == command.Seat ? afterTrade : p).ToValueArray(),
            Discard = state.Discard.Concat(command.Cards).ToValueArray(),
            Turn = state.Turn with { FreeArmies = state.Turn.FreeArmies + armies },
        };

        return Result<Transition, RuleViolation>.Ok(new Transition(
            next,
            new GameEvent[] { new CardsTraded(command.Seat, command.Cards, armies) }.ToValueArray()));
    }

    private static RuleViolation? ValidateCards(Participant participant, ValueArray<string> cards, MapDefinition map)
    {
        if (cards.Count != Size)
        {
            return new RuleViolation(
                RuleViolations.InvalidCardCount,
                $"A trade names exactly {Size} cards, not {cards.Count}.");
        }

        if (cards.Distinct().Count() != cards.Count)
        {
            return new RuleViolation(RuleViolations.DuplicateCard, "A trade cannot name the same card twice.");
        }

        var missing = cards.FirstOrDefault(card => !participant.Cards.Contains(card));
        if (missing is not null)
        {
            return new RuleViolation(RuleViolations.CardNotInHand, $"The card '{missing}' is not in your hand.");
        }

        var symbolsUsed = cards.Select(card => map.Territory(card).Symbol).Distinct().Count();
        var allSame = symbolsUsed == 1;
        var allDifferent = symbolsUsed == Enum.GetValues<CardSymbol>().Length;
        if (!allSame && !allDifferent)
        {
            return new RuleViolation(
                RuleViolations.InvalidCardCombination,
                "A trade needs three cards of one symbol, or one card of each symbol.");
        }

        return null;
    }
}
