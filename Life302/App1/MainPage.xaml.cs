﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Storage;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using System.Xml.Linq;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

/*
 * ftp://ftp.ncbi.nlm.nih.gov/refseq/H_sapiens/RefSeqGene/
 * http://www.genenames.org/cgi-bin/hgnc_downloads
 * http://asia.ensembl.org/biomart/martview
 * 
 * HPRD에서 사용가능한 데이터는 RefSeq protein(NP) ID 뿐
 * HGNC에서는 RefSeq NM ID와 앙상블 ID의 매핑을 제공
 * 앙상블은 RefSeq NM ID와 매핑을 제공은 하나 빠져있는 데이터가 많아 완전하지 못함
 * RefSeq가 RefSeq NM ID와 NP ID를 매핑해주지만 없는 gene이 있다
 * 
 * HGNC gene name 데이터도 HPRD가 제공하지만 같은 이름의 다른 gene을 가리킬 수 있어 사용 불가능
 */

namespace Life302
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            Process();
        }

        async void Process()
        {
            //await saveDrosophilaNetwork();
            //await saveDrosophilaToHumanOrtholog();
            //await saveMappedOrtholog();
            //await saveUniprotMapper();
            //await saveHumanNetwork();

            //var ortholog = await readDrosophilaToHumanOrtholog();

            await saveRValue();

            //await saveValidOrtholog();
        }

        async Task<SortedDictionary<String, String>> readValidOrtholog(SortedDictionary<String, SortedSet<String>> drosophila, SortedDictionary<String, SortedSet<String>> human)
        {
            var mapped = await readMappedOrtholog();
            var orthologsFilteredDrosophilaHuman = new Dictionary<String, String>();
            var orthologsAmbiguous = new Dictionary<String, SortedSet<String>>();

            System.Diagnostics.Debug.WriteLine("Started filtering ortholog data");
            var drosophilaGenes = drosophila.Keys;

            foreach (String drosophilaGene in drosophilaGenes)
            {
                SortedSet<String> mappedOrthologs;
                if (!mapped.TryGetValue(drosophilaGene, out mappedOrthologs))//check the gene is mapped as ortholog
                    continue;
                else//filter the genes so that they exists in the human protein network data
                {
                    var checkedOrthologs = new SortedSet<String>();
                    foreach (String tocheck in mappedOrthologs)
                        if (human.ContainsKey(tocheck))
                            checkedOrthologs.Add(tocheck);
                    if (checkedOrthologs.Count == 1)
                    {
                        orthologsFilteredDrosophilaHuman.Add(drosophilaGene, checkedOrthologs.First());
                    }
                    else if (checkedOrthologs.Count > 1)
                        orthologsAmbiguous.Add(drosophilaGene, checkedOrthologs);
                }
            }

            return new SortedDictionary<String, String>(orthologsFilteredDrosophilaHuman);
        }

        async Task saveValidOrtholog()
        {
            var drosophila = await readDrosophilaNetwork();
            var human = await readHumanNetwork();
            var ortholog = await readValidOrtholog(drosophila, human);

            FileSavePicker picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
            picker.SuggestedFileName = "ValidOrtholog";
            StorageFile savefile = await picker.PickSaveFileAsync();
            if (savefile != null)
            {
                await NetworkDataProcessor.saveStringDictionary(savefile, "Protein", "Otholog protein", ortholog);
                await new MessageDialog("Completed").ShowAsync();
            }
            else
            {
                await new MessageDialog("Canceled").ShowAsync();
            }
        }

        async Task<SortedDictionary<UInt16, SortedSet<String>>[]> readRValue()
        {
            System.Diagnostics.Debug.WriteLine("Started reading drosophila network");
            var drosophila = await readDrosophilaNetwork();
            System.Diagnostics.Debug.WriteLine("Started reading human network");
            var human = await readHumanNetwork();

            var orthologsFilteredDrosophilaHuman = await readValidOrtholog(drosophila, human);

            System.Diagnostics.Debug.WriteLine("Started calculating r values");

            var humanRemainingGenes = human.Keys.ToList();

            var orthologsRValueList = new AutoLister<UInt16, String>();
            var orthologsRValueListHuman = new AutoLister<UInt16, String>();
            var drosophilaSpecificRValueList = new AutoLister<UInt16, String>();
            var humanSpecificRValueList = new AutoLister<UInt16, String>();

            foreach (KeyValuePair<String, SortedSet<String>> drosophilaPpi in drosophila)
            {
                String humanOrtholog;
                if (orthologsFilteredDrosophilaHuman.TryGetValue(drosophilaPpi.Key, out humanOrtholog))
                {
                    SortedSet<String> drosophilaInteractionsOrthologMapped = new SortedSet<String>();
                    SortedSet<String> humanInteractions = human[humanOrtholog];
                    humanRemainingGenes.Remove(humanOrtholog);
                    foreach (String ppi in drosophilaPpi.Value)
                    {
                        String mappedInteraction;
                        if (orthologsFilteredDrosophilaHuman.TryGetValue(ppi, out mappedInteraction))
                            drosophilaInteractionsOrthologMapped.Add(mappedInteraction);
                    }
                    drosophilaInteractionsOrthologMapped.IntersectWith(humanInteractions);

                    orthologsRValueList.Add(
                        (UInt16)(drosophilaPpi.Value.Count + humanInteractions.Count - drosophilaInteractionsOrthologMapped.Count * 2), drosophilaPpi.Key);
                    orthologsRValueListHuman.Add(
                        (UInt16)(drosophilaPpi.Value.Count + humanInteractions.Count - drosophilaInteractionsOrthologMapped.Count * 2), humanOrtholog);
                }
                else // drosophila specific
                    drosophilaSpecificRValueList.Add((UInt16)drosophilaPpi.Value.Count, drosophilaPpi.Key);
            }
            foreach (String humanRemainingGene in humanRemainingGenes)
                humanSpecificRValueList.Add((UInt16)human[humanRemainingGene].Count, humanRemainingGene);

            return new SortedDictionary<UInt16, SortedSet<String>>[]
                { 
                    orthologsRValueList.GetSortedDictionary(),
                    orthologsRValueListHuman.GetSortedDictionary(),
                    drosophilaSpecificRValueList.GetSortedDictionary(), 
                    humanSpecificRValueList.GetSortedDictionary()
                };
        }

        async Task saveRValue()
        {
            var rvalues = await readRValue();

            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
                picker.SuggestedFileName = "OrthologDrosophilaIdRValue";
                StorageFile savefile = await picker.PickSaveFileAsync();
                if (savefile != null)
                {
                    await NetworkDataProcessor.saveStringSetDictionary(savefile, "r value", "Orthologs by Drosophila Gene ID", rvalues[0]);
                    await new MessageDialog("Completed").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Canceled").ShowAsync();
                }
            }

            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
                picker.SuggestedFileName = "OrthologHumanIdRValue";
                StorageFile savefile = await picker.PickSaveFileAsync();
                if (savefile != null)
                {
                    await NetworkDataProcessor.saveStringSetDictionary(savefile, "r value", "Orthologs by Human Gene ID", rvalues[1]);
                    await new MessageDialog("Completed").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Canceled").ShowAsync();
                }
            }

            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
                picker.SuggestedFileName = "DrosophilaRValue";
                StorageFile savefile = await picker.PickSaveFileAsync();
                if (savefile != null)
                {
                    await NetworkDataProcessor.saveStringSetDictionary(savefile, "r value", "Drosophila Specific Genes", rvalues[2]);
                    await new MessageDialog("Completed").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Canceled").ShowAsync();
                }
            }

            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
                picker.SuggestedFileName = "HumanRValue";
                StorageFile savefile = await picker.PickSaveFileAsync();
                if (savefile != null)
                {
                    await NetworkDataProcessor.saveStringSetDictionary(savefile, "r value", "Human Specific Proteins", rvalues[3]);
                    await new MessageDialog("Completed").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Canceled").ShowAsync();
                }
            }
        }

        async Task saveRValueSpread()
        {
            var rvalues = await readRValue();

            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
                picker.SuggestedFileName = "OrthologDrosophilaIdRValueSpread";
                StorageFile savefile = await picker.PickSaveFileAsync();
                if (savefile != null)
                {
                    await NetworkDataProcessor.saveStringSetDictionarySpread(savefile, "r value", "Orthologs by Drosophila Gene ID", rvalues[0]);
                    await new MessageDialog("Completed").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Canceled").ShowAsync();
                }
            }

            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
                picker.SuggestedFileName = "OrthologHumanIdRValueSpread";
                StorageFile savefile = await picker.PickSaveFileAsync();
                if (savefile != null)
                {
                    await NetworkDataProcessor.saveStringSetDictionarySpread(savefile, "r value", "Orthologs by Human Gene ID", rvalues[1]);
                    await new MessageDialog("Completed").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Canceled").ShowAsync();
                }
            }

            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
                picker.SuggestedFileName = "DrosophilaRValueSpread";
                StorageFile savefile = await picker.PickSaveFileAsync();
                if (savefile != null)
                {
                    await NetworkDataProcessor.saveStringSetDictionarySpread(savefile, "r value", "Drosophila Specific Genes", rvalues[2]);
                    await new MessageDialog("Completed").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Canceled").ShowAsync();
                }
            }

            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
                picker.SuggestedFileName = "HumanRValueSpread";
                StorageFile savefile = await picker.PickSaveFileAsync();
                if (savefile != null)
                {
                    await NetworkDataProcessor.saveStringSetDictionarySpread(savefile, "r value", "Human Specific Proteins", rvalues[3]);
                    await new MessageDialog("Completed").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Canceled").ShowAsync();
                }
            }
        }

        async Task<SortedDictionary<String, SortedSet<String>>> readMappedOrtholog()
        {
            var mapper = await readUniprotMapper();
            var ortholog = await readDrosophilaToHumanOrtholog();

            var newlyMappedOrtholog = new SortedDictionary<String, SortedSet<String>>();
            SortedSet<String> notMapped = new SortedSet<String>();
            foreach (KeyValuePair<String, String> pair in ortholog)
            {
                SortedSet<String> mappedProteins;
                if (mapper.TryGetValue(pair.Value, out mappedProteins))
                    newlyMappedOrtholog.Add(pair.Key, mappedProteins);
                else
                    notMapped.Add(pair.Value);
            }

            return newlyMappedOrtholog;
        }

        async Task saveMappedOrtholog()
        {
            var mapped = await readMappedOrtholog();

            FileSavePicker picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
            picker.SuggestedFileName = "MappedOrtholog";
            StorageFile savefile = await picker.PickSaveFileAsync();
            if (savefile != null)
            {
                await NetworkDataProcessor.saveStringSetDictionary(savefile, "Drosophila Protein", "Human Ortholog Proteins", mapped);
                await new MessageDialog("Completed").ShowAsync();
            }
            else
            {
                await new MessageDialog("Canceled").ShowAsync();
            }
        }

        async Task<SortedDictionary<String, SortedSet<String>>> readDrosophilaNetwork()
        {
            StorageFolder folder = await Package.Current.InstalledLocation.GetFolderAsync("DroID");
            var files = await folder.GetFilesAsync();
            return await NetworkDataProcessor.readDrosophilaNetwork(files.ToArray());
        }

        async Task saveDrosophilaNetwork()
        {
            var network = await readDrosophilaNetwork();

            FileSavePicker picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
            picker.SuggestedFileName = "DrosophilaNetwork";
            StorageFile savefile = await picker.PickSaveFileAsync();
            if (savefile != null)
            {
                await NetworkDataProcessor.saveStringSetDictionary(savefile, "Drosophila Protein", "Human Ortholog Protein", network);
                await new MessageDialog("Completed").ShowAsync();
            }
            else
            {
                await new MessageDialog("Canceled").ShowAsync();
            }
        }

        async Task<SortedDictionary<String, SortedSet<String>>> readHumanNetwork()
        {
            StorageFolder folder = await Package.Current.InstalledLocation.GetFolderAsync("HPRD");
            StorageFile file = (await folder.GetFilesAsync())[0];
            return await NetworkDataProcessor.readHumanNetwork(false, file);
        }

        async Task saveHumanNetwork()
        {
            var network = await readHumanNetwork();

            FileSavePicker picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
            picker.SuggestedFileName = "HumanNetwork";
            StorageFile savefile = await picker.PickSaveFileAsync();
            if (savefile != null)
            {
                await NetworkDataProcessor.saveStringSetDictionary(savefile, "Protein", "Protein Interaction List", network);
                await new MessageDialog("Completed").ShowAsync();
            }
            else
            {
                await new MessageDialog("Canceled").ShowAsync();
            }
        }

        async Task<SortedDictionary<String, SortedSet<String>>> readUniprotMapper()
        {
            StorageFolder mapperfolder = await Package.Current.InstalledLocation.GetFolderAsync("Mapper");
            StorageFile mapperfile = await mapperfolder.GetFileAsync("HUMAN_9606_idmapping.txt");
            return await NetworkDataProcessor.readUniprotMapper(mapperfile, false);
        }

        async Task saveUniprotMapper()
        {
            var mapper = await readUniprotMapper();

            FileSavePicker picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
            picker.SuggestedFileName = "UniprotMapper";
            StorageFile savefile = await picker.PickSaveFileAsync();
            if (savefile != null)
            {
                await NetworkDataProcessor.saveStringSetDictionary(savefile, "Ensembl Protein", "RefSeq Proteins", mapper);
                await new MessageDialog("Completed").ShowAsync();
            }
            else
            {
                await new MessageDialog("Canceled").ShowAsync();
            }
        }

        async Task<SortedDictionary<String, String>> readDrosophilaToHumanOrtholog()
        {
            StorageFolder folder = await Package.Current.InstalledLocation.GetFolderAsync("InParanoid");
            StorageFile file = (await folder.GetFilesAsync())[0];
            return await NetworkDataProcessor.readOrtholog(file, "Drosophila melanogaster", "Homo Sapiens");
        }

        async Task saveDrosophilaToHumanOrtholog()
        {
            var ortholog = await readDrosophilaToHumanOrtholog();

            FileSavePicker picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV Spreadsheet format", new List<String> { ".csv" });
            picker.SuggestedFileName = "DrosophilaToHumanOrtholog";
            StorageFile savefile = await picker.PickSaveFileAsync();
            if (savefile != null)
            {
                await NetworkDataProcessor.saveStringDictionary(savefile, "Gene Name", "Ortholog Gene Name", ortholog);
                await new MessageDialog("Completed").ShowAsync();
            }
            else
            {
                await new MessageDialog("Canceled").ShowAsync();
            }
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
        }
    }

    public static class NetworkDataProcessor
    {
        public async static Task<SortedDictionary<String, SortedSet<String>>> readDrosophilaNetwork(params StorageFile[] files)
        {
            var dictionary = new Dictionary<String, SortedSet<String>>();
            var autolister = new AutoLister<String, String>(dictionary);
            foreach (StorageFile file in files)
            {
                IList<String> str = await FileIO.ReadLinesAsync(file);
                for (Int32 i = 1; i < str.Count; i++)
                {
                    String[] splitted = str[i].Split('\t');
                    String firstgene = splitted[0];
                    String secondgene = splitted[1];

                    autolister.Add(firstgene, secondgene);
                    //if (dictionary.ContainsKey(firstgene))
                    //    dictionary[firstgene].Add(secondgene);
                    //else
                    //{
                    //    var newSet = dictionary[firstgene] = new SortedSet<String>();
                    //    newSet.Add(secondgene);
                    //}
                }
            }

            return autolister.GetSortedDictionary();
        }

        public async static Task<SortedDictionary<String, SortedSet<String>>> readHumanNetwork(Boolean deleteDot, params StorageFile[] files)
        {
            Func<String, String> func;
            if (deleteDot)
                func = delegate(String npstr) { return npstr.Split('.')[0]; };
            else
                func = delegate(String npstr) { return npstr; };
            var dictionary = new Dictionary<String, SortedSet<String>>();
            var autolister = new AutoLister<String, String>(dictionary);

            foreach (StorageFile file in files)
            {
                IList<String> str = await FileIO.ReadLinesAsync(file);
                for (Int32 i = 0; i < str.Count; i++)
                {
                    String[] splitted = str[i].Split('\t');
                    String firstprotein = func(splitted[2]);
                    String secondprotein = func(splitted[5]);

                    autolister.Add(firstprotein, secondprotein);
                    //if (dictionary.ContainsKey(firstprotein))
                    //    dictionary[firstprotein].Add(secondprotein);
                    //else
                    //{
                    //    var newSet = dictionary[firstprotein] = new SortedSet<String>();
                    //    newSet.Add(secondprotein);
                    //}
                }
            }

            return autolister.GetSortedDictionary();
        }

        public async static Task<SortedDictionary<String, String>> readOrtholog(StorageFile file, String referenceSpeciesName, String comparisonSpeciesName)
        {
            XDocument xdoc = XDocument.Parse(await FileIO.ReadTextAsync(file));

            var orthologReader = new OrthoXml.InParanoidReader(xdoc);

            return orthologReader.MapGeneToGeneSeedOrtholog(referenceSpeciesName, comparisonSpeciesName);
            //InParalog는 나중에 처리하고 일단 이건 보여주기용?
        }

        public async static Task saveStringDictionary(StorageFile file, String firstColumnName, String secondColumnName, SortedDictionary<String, String> dictionary)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteString(
                        String.Format("{0},{1}\n", firstColumnName, secondColumnName));
                    foreach (KeyValuePair<String, String> pair in dictionary)
                        writer.WriteString(String.Format("{0},{1}\n", pair.Key, pair.Value));
                    await writer.StoreAsync();
                }
            }
        }

        public async static Task<SortedDictionary<String, SortedSet<String>>> readUniprotMapper(StorageFile file, Boolean deleteDot)
        {
            Func<String, String> func;
            if (deleteDot)
                func = delegate(String npstr) { return npstr.Split('.')[0]; };
            else
                func = delegate(String npstr) { return npstr; };

            var refseqDictionary = new Dictionary<String, SortedSet<String>>();
            var ensembleproDictionary = new Dictionary<String, SortedSet<String>>();

            var dictionary = new SortedDictionary<String, SortedSet<String>>();

            IList<String> filestr = await FileIO.ReadLinesAsync(file);
            List<String> newstr = new List<String>();
            Parallel.ForEach(filestr, delegate(String str)
            {
                String[] splitted = str.Split('\t');
                switch (splitted[1])
                {
                    case "RefSeq":
                    case "Ensembl_PRO":
                        lock (newstr)
                        {
                            newstr.Add(str);
                        }
                        break;
                }
            });
            SortedSet<String> sortedstr = new SortedSet<String>(newstr);

            String currentId;
            SortedSet<String> refseqList = new SortedSet<String>();
            SortedSet<String> ensembleproList = new SortedSet<String>();
            {
                String[] splitted = sortedstr.First().Split('\t');
                currentId = splitted[0];
                if (splitted[1] == "RefSeq")
                    refseqList.Add(func(splitted[2]));
                else
                    ensembleproList.Add(splitted[2]);
            }
            foreach (String str in sortedstr)
            {
                String[] splitted = str.Split('\t');
                if (currentId != splitted[0])
                {
                    refseqDictionary.Add(currentId, refseqList);
                    ensembleproDictionary.Add(currentId, ensembleproList);

                    currentId = splitted[0];
                    refseqList = new SortedSet<String>();
                    ensembleproList = new SortedSet<String>();
                }

                if (splitted[1] == "RefSeq")
                    refseqList.Add(func(splitted[2]));
                else
                    ensembleproList.Add(splitted[2]);
            }
            refseqDictionary.Add(currentId, refseqList);
            ensembleproDictionary.Add(currentId, ensembleproList);

            foreach (KeyValuePair<String, SortedSet<String>> pair in ensembleproDictionary)
            {
                SortedSet<String> list;
                if (!refseqDictionary.TryGetValue(pair.Key, out list))
                    break;

                foreach (String str in pair.Value)
                {
                    if (list.Count != 0)
                        try
                        {
                            dictionary.Add(str, list);
                        }
                        catch
                        {
                            foreach (String item in list)
                                dictionary[str].Add(item);
                        }
                }
            }

            return dictionary;
        }

        public async static Task saveStringSetDictionary<T1>(StorageFile file, String firstColumnName, String secondColumnName, SortedDictionary<T1, SortedSet<String>> dictionary)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteString(
                        String.Format("{0},{1}\n", firstColumnName, secondColumnName));
                    foreach (KeyValuePair<T1, SortedSet<String>> pair in dictionary)
                        writer.WriteString(String.Format("{0},{1}\n", pair.Key, String.Join(",", pair.Value)));
                    await writer.StoreAsync();
                }
            }
        }

        public async static Task saveStringSetDictionarySpread<T1>(StorageFile file, String firstColumnName, String secondColumnName, SortedDictionary<T1, SortedSet<String>> dictionary)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteString(
                        String.Format("{0},{1}\n", firstColumnName, secondColumnName));
                    foreach (KeyValuePair<T1, SortedSet<String>> pair in dictionary)
                        foreach (String secondItem in pair.Value)
                            writer.WriteString(String.Format("{0},{1}\n", pair.Key, secondItem));
                    await writer.StoreAsync();
                }
            }
        }
    }

    public class AutoLister<T1, T2>
    {
        Dictionary<T1, SortedSet<T2>> dictionary;
        UInt32 listedItemCount = 0;

        public AutoLister(Dictionary<T1, SortedSet<T2>> dictionary)
        {
            this.dictionary = dictionary;
            foreach (KeyValuePair<T1, SortedSet<T2>> pair in dictionary)
                listedItemCount += (UInt32)pair.Value.Count;
        }

        public AutoLister()
        {
            this.dictionary = new Dictionary<T1, SortedSet<T2>>();
        }

        public SortedDictionary<T1, SortedSet<T2>> GetSortedDictionary()
        {
            return new SortedDictionary<T1, SortedSet<T2>>(dictionary);
        }

        public void Add(T1 level, T2 item)
        {
            SortedSet<T2> levelset;
            if (!dictionary.TryGetValue(level, out levelset))
                levelset = dictionary[level] = new SortedSet<T2>();
            levelset.Add(item);
            listedItemCount++;
        }
    }
}
