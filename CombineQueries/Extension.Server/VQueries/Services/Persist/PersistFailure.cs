using System.Data.Common;

using Microsoft.EntityFrameworkCore;

namespace CombineQueries.Api.Services.Persist;

// Отказ хранилища - и только он. Ошибки кода обязаны падать, а не притворяться «базы нет».
//
// Проверять тип самого исключения мало: EF оборачивает недоступную базу в InvalidOperationException
// («transient failure»), и настоящий DbException лежит внутри. Без обхода цепочки такой отказ
// проскакивал мимо catch, и запрос падал 500 вместо того, чтобы доработать в памяти.
public static class PersistFailure
{
    public static bool Unavailable(Exception error)
    {
        for (Exception? at = error; at is not null; at = at.InnerException)
            if (at is DbException or DbUpdateException) return true;

        return false;
    }
}
