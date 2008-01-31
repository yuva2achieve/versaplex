#!/usr/bin/python

#------------------------------------------------------------------------------
#                                  Veranda 
#                              ~--------------~
#
# Original Author: Andrei "Garoth" Thorp <garoth@gmail.com>
#
# Description: This program provides a graphical frontend to using the
#              Versaplex software. The user may provide SQL commands in the
#              editor, and this program will forward those commands over DBus
#              to Versaplexd. Versaplexd will then apply the commands to 
#              whatever database it is configured to use under the surface.
#              As such, this program eventually should be capable of providing
#              a graphical interface to many databases, using Versaplex for
#              abstraction.
#
# Notes:
#   Indentation: I use tabs only, 4 spaces per tab.
#
# Todo:
#   Add version numbers for imports
#------------------------------------------------------------------------------
import sys
import pygtk
import gtk
import gtk.glade
import gtksourceview2 as gtksourceview
import dbus
import time
from Parser import Parser
from Resulter import Resulter
#------------------------------------------------------------------------------
class MainUI:
#------------------------------------------------------------------------------
	""" Generates the GUI for Veranda"""
	#------------------	
	def __init__(self):
	#------------------
		"""Initialize all the UI stuff"""
		# Define some instance variables
		self.name = "Veranda"
		self.version = "0.8"
		self.newNumbers = [] # for naming new tabs
		self.database = DBusSql()
		self.topToBottom = {} #Links the top editors with bottom buffers

		# Import Glade's XML and load it
		self.gladeTree = gtk.glade.XML("ui.glade")
		dic = {"on_exit":(gtk.mainquit)}
		self.gladeTree.signal_autoconnect(dic)

		# Grab some of the widgets for easy access
		self.window = self.gladeTree.get_widget("window")
		self.vboxMain = self.gladeTree.get_widget("vbox-main")
		self.vpanedEditor = self.gladeTree.get_widget("vpaned-editor")
		self.vpanedPanel = self.gladeTree.get_widget("vpaned-panel")
		self.notebookTop = self.gladeTree.get_widget("notebook-top")
		self.notebookBottom = self.gladeTree.get_widget("notebook-bottom")
		self.buttonRun = self.gladeTree.get_widget("button-run")
		self.buttonNewTab = self.gladeTree.get_widget("button-newtab")
		self.tbuttonHidePanel = self.gladeTree.get_widget("tbutton-hidepanel")
		self.entrySearch = self.gladeTree.get_widget("entry-search")
		self.treeview = self.gladeTree.get_widget("treeview")

		# Open a first tab (comes with configured editor)
		self.newTab()

		# Set up the hide/show button
		self.rArrow = gtk.Arrow(gtk.ARROW_RIGHT,gtk.SHADOW_IN)
		self.lArrow = gtk.Arrow(gtk.ARROW_LEFT,gtk.SHADOW_IN)
		self.tbuttonHidePanel.add(self.lArrow)

		# Connect events
		self.window.connect("delete_event",gtk.main_quit)
		self.tbuttonHidePanel.connect("toggled",self.hidePanel)
		self.buttonRun.connect("clicked",self.runQuery)
		self.buttonNewTab.connect("clicked",self.newTab)

		# Show things
		self.rArrow.show()
		self.lArrow.show()
		self.window.show()
	
	#-----------------------	
	def getNewNumber(self):	
	#-----------------------
		""" Get a unique number to number a tab """
		x = 0
		while (True):
			if x in self.newNumbers:
				x = x+1
			else: 
				self.newNumbers.append(x)
				return r"   "+str(x)+r"   "
	
	#--------------------------------------	
	def removeNumber(self,editor,notebook):
	#--------------------------------------
		""" If a given page has a label with an automatic
		number, remove that number from the list of numbers so that
		it can be reassigned to a new fresh tab in the future"""
		self.newNumbers.remove(self.getLabelText(editor,notebook))
	
	#-----------------------------------------------	
	def configureEditor(self,editor,textbuffer):
	#-----------------------------------------------
		"""Sets up a gtksourceview with the common options I want."""
		languagemanager = gtksourceview.LanguageManager()
		textbuffer.set_language(languagemanager.get_language("sql"))
		textbuffer.set_highlight_syntax(True)
		editor.set_show_line_numbers(True)
		editor.set_wrap_mode(gtk.WRAP_WORD_CHAR)

	#-------------------------------------------
	def makeBottomTabMenu(self,number,resulter):
	#-------------------------------------------
		"""Returns an hbox with the title, change button, and close button
		to be put in a tab"""
		hbox = gtk.HBox()
		label = gtk.Label(r"   "+str(number)+r"   ")
		hbox.pack_start(label)
		
		dArrow = gtk.Arrow(gtk.ARROW_DOWN,gtk.SHADOW_IN) #TODO use image instead
		buttonMode = gtk.Button(None)

		buttonMode.add(dArrow)
		hbox.pack_start(buttonMode,False,False,1)
		buttonClose = gtk.Button(" X ") 				#TODO put image here
		hbox.pack_start(buttonClose,False,False,1) 

		buttonClose.connect("clicked",self.closeTab,resulter)
		buttonMode.connect("clicked",self.changeMode,resulter)

		dArrow.show()
		buttonMode.show()
		label.show()
		buttonClose.show()
		hbox.show()

		return hbox

	#-------------------------------------	
	def showOutput(self,topEditor,result):
	#-------------------------------------
		print "------------------------------------------------------"
		print 					      result
		print "------------------------------------------------------"
		parser = Parser(result)
		resulter = Resulter(parser)
		
		if self.topToBottom == {}:
			self.notebookBottom.show()

		if topEditor not in self.topToBottom:
			hbox = self.makeBottomTabMenu(self.getLabelText(
										  topEditor,self.notebookTop),resulter)
			self.newTabBottom(resulter.getCurrentView(),hbox)
			self.topToBottom[topEditor] = resulter

		else:
			resulter = self.topToBottom[topEditor]
			resulter.update(parser)

	#----------------------------------
	def newTabBottom(self,widget,hbox):
	#----------------------------------
		"""Creates a new tab on the bottom notebook, with "widget" in the tab
		and "hbox" as the label (not actually a gtk label)"""
		self.notebookBottom.append_page(widget,hbox)
	
	#--------------------------------------	
	def getLabelText(self,editor,notebook):
	#--------------------------------------
		"""Retrieves the label number from notebook with a page which contains 
		the given editor"""
		# FIXME make sure that it can be any child instead of editor
		hbox = notebook.get_tab_label(editor)
		children = hbox.get_children()
		labelText = children[0].get_text()
		labelText = labelText.strip(' ')
		return int(labelText)

	#----------------------#
	#-- CALLBACK METHODS --#
	#----------------------#

	#-----------------------------------	
	def runQuery(self,widget,data=None):
	#-----------------------------------
		"""Uses the database abstraction (initially Dbus)
		To send the query that is in the current window"""
		#inner call returns index, outer returns child
		#FIXME what if there is no current page?
		editor = self.notebookTop.get_nth_page(self.notebookTop.
												get_current_page()) 
		buffer = editor.get_buffer()
		 #get all text, not including hidden chars
		query = buffer.get_text(buffer.get_start_iter(),
								buffer.get_end_iter(),False)

		print "Running Query:",query

		self.showOutput(editor,self.database.query(query))
	
	#--------------------------------------	
	def newTab(self,widget=None,data=None):
	#--------------------------------------
		"""Open a new editor tab"""
		textbuffer = gtksourceview.Buffer()
		editor = gtksourceview.View(textbuffer)

		self.configureEditor(editor,textbuffer)

		hbox = gtk.HBox()
		label = gtk.Label(self.getNewNumber())
		hbox.pack_start(label)
		buttonClose = gtk.Button(" X ") #TODO put image here
		hbox.pack_start(buttonClose,False,False,1) 

		buttonClose.connect("clicked",self.closeTab,editor)

		self.notebookTop.append_page(editor,hbox)
		# FIXME Why doesn't below work?
		self.notebookTop.set_tab_reorderable(editor,True) 
		self.notebookTop.set_current_page(self.notebookTop.page_num(editor))

		label.show()
		buttonClose.show()
		hbox.show()
		editor.show()

	#------------------------------	
	def closeTab(self,widget,data): #FIXME just bloody fix me
	#------------------------------
		"""Close a tab. Data is the contents of a notebook you want killed."""
		index = self.notebookTop.page_num(data)
		if index != -1:
			self.removeNumber(data,self.notebookTop)
			self.notebookTop.remove_page(index)
			del self.topToBottom[data]
			return
		
		index = self.notebookBottom.page_num(data)
		if index != -1:
			entry = ""
			for x,y in self.topToBottom.iteritems(): 
				if y is data.get_buffer():
					entry = x
			del self.topToBottom[entry]

			self.removeNumber(data,self.notebookBottom)
			self.notebookBottom.remove_page(index)

			return

		if index == -1:
			print "Worse Than Failure: Lost The Tab!"

	#--------------------------------	
	def changeMode(self,widget,data):
	#--------------------------------
		eh, stuff

	#------------------------------------	
	def hidePanel(self,widget,data=None):
	#------------------------------------
		"""Hide/show the side panel"""
		#means it's been pressed and is toggled down
		if widget.get_active() == True: 
			self.treeview.hide()
			self.entrySearch.hide()
			widget.remove(self.lArrow)
			widget.add(self.rArrow)
			#FIXME add resize so it doesn't just waste space
		else:
			self.treeview.show()
			self.entrySearch.show()
			widget.remove(self.rArrow)
			widget.add(self.lArrow)
	
	#--------------------------#
	#-- END CALLBACK METHODS --#
	#--------------------------#

#------------------------------------------------------------------------------
class DBusSql:
#------------------------------------------------------------------------------
	""" Provides abstraction for connecting to an SQL database via 
	DBus and the Versaplex software """
	#TODO let dbus shut down nicely
	#------------------	
	def __init__(self):
	#------------------
		""" Connects to DBus and connects to versaplex"""
		print "~-------------------------------------------~"
		print "| Setting up DBus Connection                |"
		print "| If you're using a non-standard bus,       |"
		print "| export DBUS_SESSION_BUS_ADDRESS           |"
		print "| to listen to it. For testing with WvDBus, |"
		print "| it would be something like                |"
		print "| 'tcp:host=localhost,port=5432'            |"	
		print "~-------------------------------------------~"

		# FIXME Rewrite to use a config file instead of 
		# DBUS_SESSION_BUS_ADDRESS
		self.bus = dbus.SessionBus()
		# FIXME put most of this stuff in a config file
		self.versaplex = self.bus.get_object("com.versabanq.versaplex",
				                 "/com/versabanq/versaplex/db") 
		self.versaplexI = dbus.Interface(self.versaplex,dbus_interface=
				                 "com.versabanq.versaplex.db") 

	#---------------------	
	def query(self,query):
	#---------------------
		return self.versaplexI.ExecRecordset(query)

mainUI=MainUI()
gtk.main()