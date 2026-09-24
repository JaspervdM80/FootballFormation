window.liveMatch = {
    // <PageTitle> sets only the first title: HeadOutlet renders statically, so a change made on the circuit never reaches <head>.
    setTitle: (title) => {
        document.title = title;
    }
};
