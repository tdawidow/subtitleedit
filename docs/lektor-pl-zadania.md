# Lektor PL — zadania po wersji 5.0

Aktualizacja: 16 października 2026. To lista zadań, bez implementacji funkcji.

## 5.3 — PILNE: masowe tworzenie miksów audio PL

**Status: do wykonania w następnym kroku.**

Rozszerzyć istniejące zmaksymalizowane okno masowego Lektor PL zamiast budować
kolejny mechanizm miksowania. Wykorzystać sprawdzoną usługę i algorytm pojedynczego
okna Lektor PL: ścieżka oryginalna z wideo + zsynchronizowany WAV lektora TTS.

### Wejścia i jednoznaczny wybór trybu

- Wideo i jego wybrana oryginalna ścieżka audio.
- Jeden jawnie wybrany typ audio PL: **Gotowy Miks PL** albo **WAV lektora TTS**.
- Jedna aktywna kolumna wejściowego audio PL, opisana zgodnie z wybranym trybem.
  Nigdy gotowy miks i czysty WAV jednocześnie w jednym wierszu.
- Zmiana typu unieważnia poprzednią kontrolę i czyści poprzednie przypisania audio,
  aby nie przetworzyć gotowego miksu jako surowego lektora.
- Typu nie wolno zgadywać tylko po rozszerzeniu: WAV może również zawierać gotowy
  miks. Tryb jest świadomym wyborem użytkownika.
- Dla surowego lektora wskazuje się katalog WAV. Lista wideo oraz lista WAV są
  sortowane niezależnie i wiązane według numeru wiersza, jak w obecnym oknie.
- Zachować zamianę komórek przez przeciąganie, wstawianie pustej komórki,
  usuwanie komórki z przesunięciem jednej kolumny i usuwanie całych wierszy.
- Przy kilku ścieżkach oryginalnego audio umożliwić wybór ścieżki dla danego
  filmu; przed startem tabela pokazuje, która ścieżka zostanie zmiksowana.

### Działanie i wyniki

1. **Gotowy Miks PL → MKV:** zachować obecne, sprawdzone muksowanie bez
   ponownego miksowania/kodowania gotowego audio.
2. **WAV lektora TTS → Tylko miks audio:** pobrać wybraną ścieżkę oryginalną
   z wideo i zmiksować ją z lektorem według istniejącego algorytmu. Zapisuje się
   wyłącznie audio, np. `nazwa_wideo_MiksPL.ac3`, bez tworzenia MKV.
3. W tym samym oknie można udostępnić **Miks + MKV** dla wejścia WAV: najpierw
   utworzyć miks, następnie użyć obecnego mechanizmu tworzenia MKV. Priorytetem
   jest jednak brakująca operacja masowego tworzenia samego audio.
4. Gotowy miks nie wymaga osobnej akcji „Tylko miks” — jest już gotowy.
5. Wszystkie wyniki całej kolejki trafiają do jednego wskazanego katalogu.
6. Wspólne ustawienia z pojedynczego Lektor PL: tryb stały/dynamiczny,
   poziom oryginału i lektora, próg, ratio, attack/release, kodek i bitrate.
   Zachować obsługę AC3 oraz pozostałych kodeków dostępnych w obecnym module.
7. „Tylko miks audio” nie wymaga SRT ani MKVToolNix. Ukryć lub wyłączyć nieużywane
   pola napisów i wynikowego MKV; pokazywać pełną nazwę wynikowego miksu.
8. Nie zmieniać syntezy TTS, synchronizacji WAV ani sprawdzonego algorytmu
   ściszania oryginału, limitera i miksowania.

### Kontrola i kryteria odbioru

- Przed pierwszą operacją sprawdzić całą kolejkę: pliki, komplet par,
  jednoznaczny rodzaj wejścia, istnienie wybranej ścieżki oryginalnej,
  poprawność audio, katalog wynikowy i kolizje nazw.
- Wyniki nie mogą nadpisywać plików źródłowych; zasady istniejących wyników
  muszą być widoczne przed startem i zgodne z obecnym trybem masowym.
- Przetwarzać kolejno. Błąd/Stop zatrzymuje następne wiersze; ukończone wyniki
  pozostają, a pliki tymczasowe przerwanej operacji są sprzątane.
- Pokazywać stan każdego wiersza i pozostawić okno otwarte po zakończeniu.
- Porównać wyniki pojedynczej i masowej operacji dla tych samych par i ustawień:
  poprawny kodek, kanały, długość, zachowana synchronizacja i ściszanie oryginału.
- Test regresji: czysty WAV nie może zostać przypadkowo potraktowany jako gotowy
  miks; gotowy miks nie może zostać zmiksowany ponownie bez jawnej zmiany trybu.

**Stan 5.0:** okno masowego MKV już akceptuje pliki WAV, ale traktuje je jako
GOTOWE miksy (`useExistingMix: true`). Samo wybranie WAV nie realizuje powyższego
zadania — dodaje ścieżkę samego lektora, bez oryginalnego tła.

## D1 — diagnostyka przerwań TTS ustalona podczas analizy

**Status: do wykonania; bez zmiany rozliczania znaków i odzyskiwania audio.**

W logu zapisywać również powód zatrzymania i wynik ratowania pracy: rzeczywistą
wersję/build, plik zadania, indeks ostatniej ukończonej i pierwszej nieukończonej
kwestii, rodzaj błędu, HTTP/status i identyfikator żądania, jeśli są dostępne,
czasy operacji oraz stan salda/rezerwacji bez kluczy API i innych sekretów.
Rozróżnić brak lokalnego salda, limit usługi, chwilowy throttling, problem
sieciowy/niepotwierdzone żądanie i zatrzymanie przez użytkownika. Nie wyciągać
wniosku o miesięcznym limicie wyłącznie z kodu 429.

Uzasadnienie: analiza rzeczywistego przerwania potwierdziła prawidłowy zapis
ukończonych fragmentów i pozostałego SRT, ale log nie zawierał końcowego powodu
zatrzymania. Ta informacja musi być możliwa do odczytania bez zgadywania.
