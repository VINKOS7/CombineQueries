using CombineQueries.Api.Controllers.Translators.Handlers.Fragment;
using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;
using MediatR;

namespace CombineQueries.Api.Controllers.Translators.Handlers.CombineNew;

public class CombineHandler(ILogger<CombineHandler> logger, ISpeech speech) : IRequestHandler<CombineRequest, CombineResponse>
{
    public Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("CombineHandler: /init was not called");

        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: empty runes");

        int symbols = speech.SymbolsOf(TypeQuery.Fragmentate);

        bool fragmentate = Translator.HasFragment(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.RuneSize, symbols);
        
        int received = -2;

        //момент, клод мог переписать что .Accept терь ток с директ, но и нет, старый определял директ\фрагмент, но корневой, думаю если оно не так, то переписать чтобы было так
        if (!fragmentate)
        {
            // кеширование директов, на всякий, не придумал как примить еще
            speech.PushDirectRunes(request.Runes);

            received = speech.Accept(request.Runes);
        }
        // cтранно что resolve, а не IsVirtualFragment, но ладно пока, но должно IsVirtualFragment, если это экономит, если нет, то по идее он должен идти в AcceptVirtualFragment, экономя немного ресурсов
        // logger.LogWarning("combine: id {Id} is unknown VF - assembly will drop it", request.Id);
        // тогда и этот лог не имеет смысла, поэтому, да, скорее всего он там фрагменты определяет сразу, хммм, ну пока напишу как должно быть, L1 константный задаваемый клиентом 
        else if (speech.ResolveVirtualFragment(request.Id) is null) received = speech.Accept(request.Runes);
        else received = speech.AcceptVirtualFragment(request.Id);
        // еще о виртуал, тот должен сам думать о L2/3
        // еще, по идее received, еще по факту сообщает, найден ли в VFs, или иное в чем смысла нет, а вот сообщить в каком L(k) нашелся, и его id или индекс какой, можно синхронить 

        switch (received)
        {// надо как-то не обязательно эти case, но этими логами, подружить с ответом древа
            case -2: logger.LogWarning("combine: id {Id} is unknown VF - assembly will drop it", request.Id);
                break;
            case -1: logger.LogWarning("combine: id {Id} is unknown VF - assembly will drop it", request.Id);
                break;
            case 0: logger.LogWarning("combine: id {Id} is unknown VF - assembly will drop it", request.Id);
                break;
            case 1: logger.LogInformation("combine: id {Id} accepted", request.Id);
                break;
            case 2: logger.LogInformation("combine: id {Id} accepted and completed", request.Id);
                break;
            case -3:
                logger.LogInformation("combine: id {Id} accepted and completed from FL1", request.Id);
                break;
            case -4:
                logger.LogInformation("combine: id {Id} accepted and completed from VFL2", request.Id);
                break;
            case -5:
                logger.LogInformation("combine: id {Id} accepted and completed from VFL3", request.Id);
                break;
            default: logger.LogInformation("combine: rune {Received} accepted, {Kind}", received, fragmentate ? "with fragments" : "direct");
                break;
        }

        return Task.FromResult(new CombineResponse { Received = received });
    }
}
