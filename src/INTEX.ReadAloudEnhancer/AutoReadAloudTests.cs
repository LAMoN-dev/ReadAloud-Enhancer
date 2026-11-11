using System;
using System.Linq;

namespace Intex {
 static class AutoReadAloudTests {
  static DateTime now=new DateTime(2026,11,1,12,0,0,DateTimeKind.Utc);
  static ResponseFrame Frame(bool generating,params string[] keys){return new ResponseFrame{Context="window1|document1",Conversation="conversation1",Generating=generating,SendAvailable=!generating,WindowVisible=true,Reads=keys.Select(x=>new ReadAloudControl{Key=x,Enabled=true,Visible=true,Off=true}).ToList()};}
  static void Check(bool ok,string why){if(!ok)throw new Exception("Auto Read aloud: "+why);}
  static TriggerCoordinator Cycle(params string[] old){var c=new TriggerCoordinator();c.Observe(Frame(false,old),now,false);c.Observe(Frame(true,old),now.AddSeconds(1),false);return c;}
  public static void Run(string report){
   int scenarios=0;TriggerCoordinator c;
   c=new TriggerCoordinator();Check(c.Observe(Frame(false,"old"),now,false)==null&&c.Observe(Frame(false,"old"),now.AddSeconds(2),false)==null,"odtworzono stara odpowiedz przy podlaczeniu");scenarios++;
   c=Cycle("old");Check(c.Observe(Frame(false,"old","new"),now.AddSeconds(2),false)==null,"nie odczekano stabilnego przejscia");Check(c.Observe(Frame(false,"old","new"),now.AddSeconds(3),false)=="new","nie wskazano nowej odpowiedzi");Check(c.Observe(Frame(false,"old","new"),now.AddSeconds(4),false)==null,"ponowiono ta sama probe");scenarios++;
   c=Cycle();Check(c.Observe(Frame(true),now.AddSeconds(60),false)==null,"pauza tekstu uznana za koniec");scenarios++;
   c=Cycle();c.Observe(Frame(false,"new"),now.AddSeconds(2),true);Check(c.Observe(Frame(false,"new"),now.AddSeconds(9),true)==null,"przerwano zajety odtwarzacz");Check(c.Observe(Frame(false,"new"),now.AddSeconds(10),false)=="new","nie poczekano na zwolnienie odtwarzacza");scenarios++;
   c=Cycle();var native=Frame(false,"new");native.NativeReading=true;c.Observe(native,now.AddSeconds(2),false);Check(c.Observe(native,now.AddSeconds(9),false)==null,"przerwano natywny glos");Check(c.Observe(Frame(false,"new"),now.AddSeconds(10),false)=="new","nie wznowiono oczekiwania po natywnym glosie");scenarios++;
   c=Cycle("old");Check(c.Observe(Frame(false,"replacement","new"),now.AddSeconds(2),false)==null&&c.Observe(Frame(false,"replacement","new"),now.AddSeconds(4),false)==null,"przebudowe drzewa potraktowano jak nowa odpowiedz");scenarios++;
   c=Cycle();Check(c.Observe(Frame(false,"a","b"),now.AddSeconds(2),false)==null&&c.Observe(Frame(false,"a","b"),now.AddSeconds(4),false)==null,"wybrano arbitralnie jeden z dwoch przyciskow");scenarios++;
   c=Cycle();Check(c.Observe(Frame(false),now.AddSeconds(2),false)==null&&c.Observe(Frame(false),now.AddSeconds(4),false)==null,"przerwana generacja uruchomila odczyt");scenarios++;
   c=Cycle();var changed=Frame(false,"new");changed.Context="window1|conversation2";Check(c.Observe(changed,now.AddSeconds(2),false)==null&&c.Observe(changed,now.AddSeconds(4),false)==null,"nawigacja uruchomila stara probe");scenarios++;
   c=Cycle();var hidden=Frame(false,"new");hidden.Reads[0].Visible=false;c.Observe(hidden,now.AddSeconds(2),false);Check(c.Observe(hidden,now.AddSeconds(4),false)==null,"uruchomiono niewidoczny przycisk");Check(c.Observe(Frame(false,"new"),now.AddSeconds(5),false)=="new","nie wznowiono po pokazaniu przycisku");scenarios++;
   c=Cycle();var manual=Frame(false,"new");manual.Reads[0].Off=false;c.Observe(manual,now.AddSeconds(2),false);Check(c.Observe(Frame(false,"new"),now.AddSeconds(5),false)==null,"ponownie odtworzono odpowiedz przeczytana recznie");scenarios++;
   c=Cycle();c.Observe(Frame(false,"new"),now.AddSeconds(2),true);Check(c.Observe(Frame(false,"new"),now.AddMinutes(6),false)==null,"wywolano przeterminowana probe");scenarios++;
   c=new TriggerCoordinator();c.Observe(Frame(true),now,false);Check(c.Observe(Frame(false,"new"),now.AddSeconds(5),false)==null&&c.Observe(Frame(false,"new"),now.AddSeconds(6),false)==null,"podlaczenie w srodku generacji nie ustawilo nowego punktu odniesienia");scenarios++;
   c=Cycle();c.Observe(Frame(false,"a"),now.AddSeconds(2),false);Check(c.Observe(Frame(false,"b"),now.AddSeconds(3),false)==null&&c.Observe(Frame(false,"b"),now.AddSeconds(5),false)==null,"zamieniona kontrolka zostala automatycznie wywolana");scenarios++;
   c=Cycle();var gap=Frame(false);gap.SendAvailable=false;Check(c.Observe(gap,now.AddSeconds(2),false)==null,"pusty stan przejsciowy uznano za koniec");c.Observe(Frame(false,"new"),now.AddSeconds(3),false);Check(c.Observe(Frame(false,"new"),now.AddSeconds(4),false)=="new","pominieto poprawne zakonczenie po stanie przejsciowym");scenarios++;
   c=Cycle();var minimized=Frame(false,"new");minimized.WindowVisible=false;c.Observe(minimized,now.AddSeconds(2),false);Check(c.Observe(Frame(false,"new"),now.AddSeconds(4),false)==null,"przywrocenie okna odtworzylo nieobserwowana odpowiedz");scenarios++;
   c=new TriggerCoordinator();var empty=Frame(false);empty.Conversation="";c.Observe(empty,now,false);c.Observe(Frame(true),now.AddSeconds(1),false);c.Observe(Frame(false,"first"),now.AddSeconds(2),false);Check(c.Observe(Frame(false,"first"),now.AddSeconds(3),false)=="first","utworzenie kontenera pustej rozmowy zgubilo pierwsza odpowiedz");scenarios++;
   c=Cycle("old");var other=Frame(false,"old","new");other.Conversation="conversation2";Check(c.Observe(other,now.AddSeconds(2),false)==null&&c.Observe(other,now.AddSeconds(4),false)==null,"zmiana kontenera w tym samym dokumencie uruchomila odczyt");scenarios++;
   if(report!=null)Json.Save(report,new{ok=true,version="v0.12",scenarios=scenarios,realToggleCalls=0,oldResponsesIgnored=true,busyPlaybackProtected=true,navigationCancels=true,atMostOneAttempt=true,ambiguousButtonsSkipped=true,manualReadRespected=true});
  }
 }
}
