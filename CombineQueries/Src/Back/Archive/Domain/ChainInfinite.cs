// Архив (2026-09-26): прежняя адресация Infinite - цепь «заёма» по ёмкости. Снята с перехода на
// адрес по дереву (финитный предок + номер среди его Inf-потомков, см. Speech.AddressInfinite):
// предка теперь пишет Translator.Learn в Jump, End не пишется. Лежит для истории, не компилируется.
//
// Была методом агрегата Translator, звалась из TailHandler.Persist при переполнении.

//    // Сшивает Infinite-строки в цепочки «заёма по одному фрагменту».
//    //
//    // Бесконечная строка своего печёного адреса не имеет и ЗАНИМАЕТ его у финитной: якорь =
//    // id % capacity, а следующее звено той же цепи лежит ровно через ёмкость - Jump = id + capacity.
//    // Значит адрес раскладывается на «якорь + k хопов», где k = id / capacity, и каждый хоп стоит
//    // ровно один запрос. End держит готовый результат, чтобы tail не проходил цепь заново.
//    public void ChainInfinite(int capacity)
//    {
//        if (capacity <= 0) return;
//
//        var byId = new Dictionary<int, VirtualFragment>();
//
//        foreach (var fragment in VirtualFragments) byId[fragment.Id] = fragment;
//
//        foreach (var fragment in VirtualFragments)
//        {
//            if (fragment.Level != FragmentLevel.Infinite) continue;
//
//            fragment.Jump = byId.ContainsKey(fragment.Id + capacity) ? fragment.Id + capacity : null;
//            fragment.End = fragment.Text;
//        }
//    }
//
//    // Вызов в TailHandler.Persist:
//    // Цепь Infinite пересшиваем, только если в неё реально что-то добавилось.
//    if (learned.Overflowed.Count > 0) translator.ChainInfinite(speech.DfaSize * speech.PageCount);
