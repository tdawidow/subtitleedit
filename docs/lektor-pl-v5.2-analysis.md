# Lektor PL 5.2 — analiza i naprawa masowego MKV

Baza: działająca wersja 5.1, commit
`94a159ce00f426a2849b13f71d4e1c71e92e6447`, gałąź
`azure-f0-lektor-pl-v4`. Zakres: okno i kolejka masowego MKV.

## Przyczyny i porównanie z MIX PL

| Obszar | Błąd lub różnica w MKV 5.1 | Korekta w 5.2 |
| --- | --- | --- |
| Widoczna tabela | Podczas przebudowy Avalonia przekazuje pusty wiersz do szablonu. MKV dereferencjonował go i zgłaszał `NullReferenceException`. MIX PL miał zabezpieczenie. | Takie samo zabezpieczenie pustego wiersza jak w MIX PL. Wczytywanie kolejnych kolumn, edycja, czyszczenie i kolejny zestaw nie przerywają pracy. |
| Katalog wynikowy | Obsługa `TextChanged` mogła wykonać się po Start i zostać pominięta jako zmiana podczas pracy. MIX PL reagował bezpośrednio na zmianę właściwości pola. | Natychmiastowa aktualizacja tabeli oraz ponowny odczyt widocznego pola przy Start. Puste pole nie używa starego katalogu. |
| Kontrola audio | FFmpeg potrafi opisać uszkodzony plik AC3 jako ścieżkę audio bez znanej długości. Taki plik przechodził wstępną kontrolę MKV. | Wymagana jedna ścieżka audio o dodatniej, znanej długości, tak jak przy kontroli audio w MIX PL. |
| Kontrola kolejki | Stan sprawdzanego pliku nie był przekazywany do głównego komunikatu. | Widoczny numer wiersza, etap oraz błąd kontroli. Sprawdzana jest cała kolejka przed pierwszym wynikiem. |
| Zatrzymanie | Stop pomiędzy plikami mógł pozostawić następny wiersz w stanie gotowości. | Stan „Przerwano”; wcześniejsze wyniki pozostają, następne nie powstają. |
| Zapis | Plik utworzony przez inny proces po kontroli nazw mógł zostać zastąpiony. MIX PL miał dodatkowy zapis bez nadpisywania. | Taki sam wzorzec jak w MIX PL: sprawdzony plik tymczasowy, przeniesienie bez nadpisywania i sprzątanie po błędzie lub Stop. |

Dwa pierwsze błędy odtworzono testami na niezmienionej wersji 5.1: oba testy
zakończyły się niepowodzeniem. Sekwencja wczytywania i edycji widocznej tabeli
przechodziła w działającym MIX PL. Po poprawce oba testy przechodzą.

## Zakres zachowany

Bez zmian pozostały `LektorExportService`, `LektorMkvMuxer`, mikser audio,
łączenie odzyskanych części lektora, kolejka MIX PL i obsługa salda TTS.
W pozostałych oknach zmieniono jedynie oznaczenie wersji.
Masowy MKV nadal przyjmuje wideo, gotowy MIX PL i opcjonalne wymuszone SRT PL.
Nie dodano do niego miksowania czystego WAV z oryginałem.

## Weryfikacja

Pełna regresja: **126/126 zaliczonych, 0 błędów, 0 pominiętych**.
Nowy zestaw dziewięciu testów obejmuje widoczną tabelę, rzeczywiste przyciski
wczytywania i menu komórek, natychmiastową zmianę katalogu, powrót do pracy po
błędzie, dwa kolejne zestawy w jednym oknie, zachowanie ścieżek oryginalnych,
czasów klatek FFV1/H.264 z klatkami B, AC3/FLAC, opcjonalnych napisów PL,
flag default/forced, zatrzymania i kolizji powstałej już podczas pracy.
Porównywane są także sumy SHA-256 plików źródłowych przed i po muksowaniu.

Wynik pełnej regresji oraz dane kompilacji są dołączone do paczki portable
w pliku `Weryfikacja-v5.2.txt` i raportach TRX. Testy są wykonywane na Linuksie
z prawdziwym FFmpeg i MKVToolNix oraz renderowanym interfejsem Avalonia.
Program portable jest kompilowany dla Windows x64; lokalna weryfikacja
nie stanowi uruchomienia EXE w systemie Windows.
